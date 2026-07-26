using FocusAI.Application.Common.Interfaces;
using FocusAI.Application.Dtos;
using FocusAI.Domain.Entities.Trends;
using FocusAI.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FocusAI.Application.Features.Trends;

/// <summary>"Aylık Trend" from PRD section 5.9 — what the industry actually talked about.</summary>
public sealed record GetTrendsQuery(DigestPeriod Period = DigestPeriod.Monthly, int Take = 20)
    : IRequest<IReadOnlyList<TrendDto>>;

public sealed class GetTrendsQueryHandler(
    IApplicationDbContext db,
    IDateTimeProvider clock) : IRequestHandler<GetTrendsQuery, IReadOnlyList<TrendDto>>
{
    /// <summary>Number of prior periods returned alongside the current one, for the sparkline.</summary>
    private const int HistoryLength = 6;

    public async Task<IReadOnlyList<TrendDto>> Handle(
        GetTrendsQuery request,
        CancellationToken cancellationToken)
    {
        var take = Math.Clamp(request.Take, 1, 50);
        var currentStart = PeriodStart(DateOnly.FromDateTime(clock.UtcNow.UtcDateTime), request.Period);

        var current = await db.TrendSnapshots
            .AsNoTracking()
            .Where(t => t.Period == request.Period && t.PeriodStart == currentStart)
            .OrderByDescending(t => t.WeightedImportance)
            .Take(take)
            .Select(t => new
            {
                t.TopicId,
                Name = t.Topic!.Name,
                Slug = t.Topic.Slug,
                t.StoryCount,
                t.SourceCount,
                t.WeightedImportance,
                t.MomentumPercent
            })
            .ToListAsync(cancellationToken);

        if (current.Count == 0)
        {
            return [];
        }

        var topicIds = current.Select(c => c.TopicId).ToList();

        var history = await db.TrendSnapshots
            .AsNoTracking()
            .Where(t => t.Period == request.Period &&
                        topicIds.Contains(t.TopicId) &&
                        t.PeriodStart <= currentStart)
            .OrderByDescending(t => t.PeriodStart)
            .Select(t => new { t.TopicId, t.PeriodStart, t.StoryCount, t.WeightedImportance })
            .ToListAsync(cancellationToken);

        var historyByTopic = history
            .GroupBy(h => h.TopicId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(h => h.PeriodStart)
                    .Take(HistoryLength)
                    .OrderBy(h => h.PeriodStart)
                    .Select(h => new TrendPointDto(h.PeriodStart, h.StoryCount, h.WeightedImportance))
                    .ToList());

        return current
            .Select(c => new TrendDto(
                c.TopicId,
                c.Name,
                c.Slug,
                c.StoryCount,
                c.SourceCount,
                c.WeightedImportance,
                c.MomentumPercent,
                historyByTopic.TryGetValue(c.TopicId, out var points) ? points : []))
            .ToList();
    }

    internal static DateOnly PeriodStart(DateOnly date, DigestPeriod period) => period switch
    {
        DigestPeriod.Monthly => new DateOnly(date.Year, date.Month, 1),
        DigestPeriod.Weekly => date.AddDays(-(((int)date.DayOfWeek + 6) % 7)),
        _ => date
    };
}

/// <summary>Recomputes trend buckets. Scheduled nightly; safe to re-run.</summary>
public sealed record RebuildTrendsCommand(DigestPeriod Period = DigestPeriod.Monthly) : IRequest<int>;

public sealed class RebuildTrendsCommandHandler(
    IApplicationDbContext db,
    IDateTimeProvider clock) : IRequestHandler<RebuildTrendsCommand, int>
{
    public async Task<int> Handle(RebuildTrendsCommand request, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var periodStart = GetTrendsQueryHandler.PeriodStart(today, request.Period);
        var previousStart = request.Period == DigestPeriod.Monthly
            ? periodStart.AddMonths(-1)
            : periodStart.AddDays(-7);

        var windowStart = new DateTimeOffset(previousStart.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        var raw = await db.StoryTopics
            .AsNoTracking()
            .Where(st => st.Story!.Status == StoryStatus.Published && st.Story.PublishedAt >= windowStart)
            .Select(st => new
            {
                st.TopicId,
                st.Story!.PublishedAt,
                st.Story.ImportanceScore,
                st.Story.SourceCount
            })
            .ToListAsync(cancellationToken);

        var buckets = raw
            .GroupBy(r => new
            {
                r.TopicId,
                Start = GetTrendsQueryHandler.PeriodStart(DateOnly.FromDateTime(r.PublishedAt.UtcDateTime), request.Period)
            })
            .Select(g => new
            {
                g.Key.TopicId,
                g.Key.Start,
                StoryCount = g.Count(),
                SourceCount = g.Sum(x => x.SourceCount),
                WeightedImportance = g.Sum(x => x.ImportanceScore)
            })
            .ToList();

        var existing = await db.TrendSnapshots
            .Where(t => t.Period == request.Period && t.PeriodStart >= previousStart)
            .ToListAsync(cancellationToken);

        db.TrendSnapshots.RemoveRange(existing);

        var previousCounts = buckets
            .Where(b => b.Start == previousStart)
            .ToDictionary(b => b.TopicId, b => b.StoryCount);

        foreach (var bucket in buckets)
        {
            // Momentum only makes sense against a non-zero baseline; a topic with
            // no prior coverage reports 100% rather than a division by zero.
            double momentum = 0d;
            if (bucket.Start == periodStart)
            {
                momentum = previousCounts.TryGetValue(bucket.TopicId, out var previous) && previous > 0
                    ? (bucket.StoryCount - previous) / (double)previous * 100d
                    : 100d;
            }

            db.TrendSnapshots.Add(new TrendSnapshot
            {
                TopicId = bucket.TopicId,
                Period = request.Period,
                PeriodStart = bucket.Start,
                StoryCount = bucket.StoryCount,
                SourceCount = bucket.SourceCount,
                WeightedImportance = bucket.WeightedImportance,
                MomentumPercent = Math.Round(momentum, 2),
                CalculatedAt = clock.UtcNow
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        return buckets.Count;
    }
}
