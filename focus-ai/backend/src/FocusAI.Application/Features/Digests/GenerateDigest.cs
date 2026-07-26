using FocusAI.Application.Common.Interfaces;
using FocusAI.Application.Common.Mappings;
using FocusAI.Application.Common.Services;
using FocusAI.Domain.Entities.Digests;
using FocusAI.Domain.Enums;
using FocusAI.Domain.Scoring;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FocusAI.Application.Features.Digests;

/// <summary>
/// Builds (or rebuilds) one edition. Called by the 08:00 job for every active
/// reader, and on demand when someone opens the app before the job has run.
/// </summary>
/// <param name="UserId">Null builds the shared edition served to anonymous visitors.</param>
public sealed record GenerateDigestCommand(
    Guid? UserId,
    DateOnly? Date = null,
    DigestPeriod Period = DigestPeriod.Daily,
    bool Force = false) : IRequest<Guid>;

public sealed class GenerateDigestCommandHandler(
    IApplicationDbContext db,
    IReaderContextFactory readerContextFactory,
    IContentAiService ai,
    IDateTimeProvider clock,
    ILogger<GenerateDigestCommandHandler> logger) : IRequestHandler<GenerateDigestCommand, Guid>
{
    private const int CandidatePoolSize = 250;

    public async Task<Guid> Handle(GenerateDigestCommand request, CancellationToken cancellationToken)
    {
        var snapshot = await readerContextFactory.BuildAsync(request.UserId, cancellationToken);
        var date = request.Date ?? clock.TodayIn(snapshot.TimeZone);

        var existing = await db.Digests
            .Include(d => d.Items)
            .FirstOrDefaultAsync(
                d => d.UserId == request.UserId && d.Date == date && d.Period == request.Period,
                cancellationToken);

        if (existing is not null && !request.Force)
        {
            return existing.Id;
        }

        var lookbackHours = request.Period switch
        {
            DigestPeriod.Weekly => 24 * 7,
            DigestPeriod.Monthly => 24 * 30,
            _ => 30
        };

        var take = request.Period switch
        {
            // PRD 5.8: the weekly edition carries the 20 biggest developments.
            DigestPeriod.Weekly => 20,
            DigestPeriod.Monthly => 30,
            _ => snapshot.DailyStoryCount
        };

        var cutoff = clock.UtcNow.AddHours(-lookbackHours);

        var candidates = await db.Stories
            .AsNoTracking()
            .Where(s => s.Status == StoryStatus.Published && s.PublishedAt >= cutoff)
            .Where(StoryFilters.VisibleToReaders)
            .OrderByDescending(s => s.ImportanceScore)
            .Take(CandidatePoolSize)
            .Select(s => new
            {
                s.Id,
                s.Title,
                s.Category,
                s.ImportanceScore,
                s.TrustScore,
                s.ReadingMinutes,
                s.PublishedAt,
                s.Embedding,
                PrimarySourceId = s.Articles.Where(a => a.IsPrimary).Select(a => a.SourceId).FirstOrDefault(),
                Topics = s.Topics.Select(t => new { t.TopicId, t.Weight }).ToList()
            })
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
        {
            logger.LogWarning("FocusAI digest for {Date} has no candidate stories", date);
        }

        var ranked = candidates
            .Select(c =>
            {
                var result = PersonalizationScorer.Score(
                    new RankableStory
                    {
                        StoryId = c.Id,
                        ImportanceScore = c.ImportanceScore,
                        TrustScore = c.TrustScore,
                        PublishedAt = c.PublishedAt,
                        Embedding = c.Embedding,
                        PrimarySourceId = c.PrimarySourceId,
                        Topics = c.Topics.ToDictionary(t => t.TopicId, t => t.Weight)
                    },
                    snapshot.Context);

                return (Candidate: c, Result: result);
            })
            .Where(x => !x.Result.IsSuppressed)
            .Select(x => new DigestCandidate(
                x.Candidate.Id,
                x.Result.Score,
                x.Candidate.Category,
                x.Candidate.ReadingMinutes,
                x.Result.Reason))
            .ToList();

        var selected = DigestComposer.Compose(ranked, take);

        var headlines = selected
            .Select(s => candidates.First(c => c.Id == s.StoryId).Title)
            .ToList();

        var intro = headlines.Count == 0
            ? null
            : await ai.WriteDigestIntroAsync(headlines, snapshot.Language, cancellationToken);

        if (existing is not null)
        {
            db.DigestItems.RemoveRange(existing.Items);
            db.Digests.Remove(existing);
            await db.SaveChangesAsync(cancellationToken);
        }

        var digest = new Digest
        {
            UserId = request.UserId,
            Period = request.Period,
            Date = date,
            Intro = intro,
            Language = snapshot.Language,
            ReadingMinutes = DigestComposer.EstimateReadingMinutes(selected),
            GeneratedAt = clock.UtcNow,
            CreatedAt = clock.UtcNow
        };

        var rank = 1;
        foreach (var candidate in selected)
        {
            digest.Items.Add(new DigestItem
            {
                DigestId = digest.Id,
                StoryId = candidate.StoryId,
                Rank = rank++,
                Reason = candidate.Reason,
                Score = candidate.Score
            });
        }

        db.Digests.Add(digest);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "FocusAI generated {Period} digest {DigestId} for {UserId} with {Count} stories",
            request.Period,
            digest.Id,
            request.UserId?.ToString() ?? "shared",
            digest.Items.Count);

        return digest.Id;
    }
}
