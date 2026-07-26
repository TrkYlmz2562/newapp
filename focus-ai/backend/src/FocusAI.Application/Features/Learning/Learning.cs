using FocusAI.Application.Common.Exceptions;
using FocusAI.Application.Common.Interfaces;
using FocusAI.Application.Common.Services;
using FocusAI.Application.Dtos;
using FocusAI.Domain.Entities.Learning;
using FocusAI.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FocusAI.Application.Features.Learning;

/// <summary>
/// "Öğrenme Modu" (PRD 5.7): one concrete thing to learn today, sized to the
/// user's daily budget and justified by what is actually happening in the feed.
/// </summary>
public sealed record GetTodayLearningQuery : IRequest<LearningSuggestionDto?>;

public sealed class GetTodayLearningQueryHandler(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    IReaderContextFactory readerContextFactory,
    IContentAiService ai,
    IDateTimeProvider clock) : IRequestHandler<GetTodayLearningQuery, LearningSuggestionDto?>
{
    public async Task<LearningSuggestionDto?> Handle(
        GetTodayLearningQuery request,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            throw new UnauthorizedException();
        }

        var snapshot = await readerContextFactory.BuildAsync(userId, cancellationToken);
        var today = clock.TodayIn(snapshot.TimeZone);

        var existing = await LoadAsync(userId, today, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var profile = await db.UserProfiles
            .AsNoTracking()
            .Where(p => p.UserId == userId)
            .Select(p => new { p.DailyLearningMinutes })
            .FirstOrDefaultAsync(cancellationToken);

        var minutes = profile?.DailyLearningMinutes ?? 15;

        // Ground the suggestion in the last few days of high-importance coverage
        // so "why now?" has a real answer rather than a generic study plan.
        var recentHeadlines = await db.Stories
            .AsNoTracking()
            .Where(s => s.Status == StoryStatus.Published &&
                        s.PublishedAt >= clock.UtcNow.AddDays(-7))
            .OrderByDescending(s => s.ImportanceScore)
            .Take(25)
            .Select(s => s.Title)
            .ToListAsync(cancellationToken);

        var suggestion = await ai.SuggestLearningAsync(
            snapshot.InterestSlugs,
            recentHeadlines,
            minutes,
            snapshot.Language,
            cancellationToken);

        if (suggestion is null)
        {
            return null;
        }

        Guid? topicId = null;
        if (!string.IsNullOrWhiteSpace(suggestion.TopicSlug))
        {
            var slug = suggestion.TopicSlug.Trim().ToLowerInvariant();
            topicId = await db.Topics
                .Where(t => t.Slug == slug)
                .Select(t => (Guid?)t.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var entity = new LearningSuggestion
        {
            UserId = userId,
            Date = today,
            TopicId = topicId,
            Title = suggestion.Title,
            Rationale = suggestion.Rationale,
            EstimatedMinutes = suggestion.EstimatedMinutes,
            Status = LearningStatus.Suggested,
            CreatedAt = clock.UtcNow
        };

        var position = 0;
        foreach (var resource in suggestion.Resources)
        {
            entity.Resources.Add(new LearningResource
            {
                LearningSuggestionId = entity.Id,
                Title = resource.Title,
                Url = resource.Url,
                Kind = resource.Kind,
                EstimatedMinutes = resource.EstimatedMinutes,
                Position = position++
            });
        }

        db.LearningSuggestions.Add(entity);
        await db.SaveChangesAsync(cancellationToken);

        return await LoadAsync(userId, today, cancellationToken);
    }

    private Task<LearningSuggestionDto?> LoadAsync(
        Guid userId,
        DateOnly date,
        CancellationToken cancellationToken) =>
        db.LearningSuggestions
            .AsNoTracking()
            .Where(l => l.UserId == userId && l.Date == date)
            .Select(l => new LearningSuggestionDto(
                l.Id,
                l.Date,
                l.Title,
                l.Rationale,
                l.EstimatedMinutes,
                l.Status,
                l.Topic == null ? null : new TopicDto(l.Topic.Id, l.Topic.Name, l.Topic.Slug, l.Topic.Kind),
                l.Resources
                    .OrderBy(r => r.Position)
                    .Select(r => new LearningResourceDto(r.Title, r.Url, r.Kind, r.EstimatedMinutes))
                    .ToList()))
            .FirstOrDefaultAsync(cancellationToken)!;
}

public sealed record UpdateLearningStatusCommand(Guid SuggestionId, LearningStatus Status) : IRequest<Unit>;

public sealed class UpdateLearningStatusCommandHandler(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    IDateTimeProvider clock) : IRequestHandler<UpdateLearningStatusCommand, Unit>
{
    public async Task<Unit> Handle(UpdateLearningStatusCommand request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            throw new UnauthorizedException();
        }

        var suggestion = await db.LearningSuggestions
            .FirstOrDefaultAsync(l => l.Id == request.SuggestionId && l.UserId == userId, cancellationToken)
            ?? throw NotFoundException.For("Öğrenme önerisi", request.SuggestionId);

        var wasCompleted = suggestion.Status == LearningStatus.Completed;
        suggestion.Status = request.Status;
        suggestion.UpdatedAt = clock.UtcNow;

        if (request.Status == LearningStatus.Completed)
        {
            suggestion.CompletedAt = clock.UtcNow;

            // Guard against double-counting when a completed item is re-submitted.
            if (!wasCompleted)
            {
                var streak = await db.UserStreaks.FirstOrDefaultAsync(s => s.UserId == userId, cancellationToken);
                if (streak is not null)
                {
                    streak.CompletedLearnings++;
                }
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

public sealed record GetLearningHistoryQuery(int Take = 30) : IRequest<IReadOnlyList<LearningSuggestionDto>>;

public sealed class GetLearningHistoryQueryHandler(
    IApplicationDbContext db,
    ICurrentUser currentUser) : IRequestHandler<GetLearningHistoryQuery, IReadOnlyList<LearningSuggestionDto>>
{
    public async Task<IReadOnlyList<LearningSuggestionDto>> Handle(
        GetLearningHistoryQuery request,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            throw new UnauthorizedException();
        }

        return await db.LearningSuggestions
            .AsNoTracking()
            .Where(l => l.UserId == userId)
            .OrderByDescending(l => l.Date)
            .Take(Math.Clamp(request.Take, 1, 100))
            .Select(l => new LearningSuggestionDto(
                l.Id,
                l.Date,
                l.Title,
                l.Rationale,
                l.EstimatedMinutes,
                l.Status,
                l.Topic == null ? null : new TopicDto(l.Topic.Id, l.Topic.Name, l.Topic.Slug, l.Topic.Kind),
                l.Resources
                    .OrderBy(r => r.Position)
                    .Select(r => new LearningResourceDto(r.Title, r.Url, r.Kind, r.EstimatedMinutes))
                    .ToList()))
            .ToListAsync(cancellationToken);
    }
}
