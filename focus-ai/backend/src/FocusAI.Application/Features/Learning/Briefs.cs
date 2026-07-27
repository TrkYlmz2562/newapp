using FocusAI.Application.Common.Exceptions;
using FocusAI.Application.Common.Interfaces;
using FocusAI.Application.Dtos;
using FocusAI.Domain.Entities.Content;
using FocusAI.Domain.Entities.Learning;
using FocusAI.Domain.Enums;
using FocusAI.Domain.Learning;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FocusAI.Application.Features.Learning;

/// <summary>
/// Turning saved stories into lessons: the reader queues one, then asks for the
/// prompt when they are ready to sit down with it.
/// </summary>
/// <remarks>
/// The two steps are separate commands because they cost different things.
/// Queueing is a row; generating is a model call. Auto-generating on entry to the
/// Öğren tab would spend a call on every story the reader ever saved, most of
/// which they will never study.
/// </remarks>
public static class BriefQueries
{
    /// <summary>How many stories one lesson may carry.</summary>
    internal const int MaxStoriesPerBrief = 5;

    /// <summary>
    /// Loads the story graph and folds it into the shape the composer takes.
    /// </summary>
    internal static async Task<IReadOnlyList<BriefStoryFile>> LoadStoryFilesAsync(
        IApplicationDbContext db,
        IReadOnlyList<Guid> storyIds,
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (storyIds.Count == 0)
        {
            return [];
        }

        // Loaded as entities rather than projected: Comparison.Points is a jsonb
        // value converter, and the reading of it belongs in memory where the
        // converter runs, not in a projection that has to be translated.
        var stories = await db.Stories
            .AsNoTracking()
            .Include(s => s.Summary)
            .Include(s => s.Trust)
            .Include(s => s.Comparison)
            .Include(s => s.Commitment)
            .Include(s => s.Topics).ThenInclude(t => t.Topic)
            .Include(s => s.Articles).ThenInclude(a => a.Source)
            .Where(s => storyIds.Contains(s.Id))
            .ToListAsync(cancellationToken);

        // The reader's own note lives on the bookmark, not the story, and it is
        // often the most specific thing in the whole file — it says what they
        // actually wanted to know.
        var notes = await db.Bookmarks
            .AsNoTracking()
            .Where(b => b.UserId == userId && storyIds.Contains(b.StoryId) && b.Note != null)
            .ToDictionaryAsync(b => b.StoryId, b => b.Note!, cancellationToken);

        // Preserve the caller's ordering; the database does not promise one.
        return storyIds
            .Select(id => stories.FirstOrDefault(s => s.Id == id))
            .Where(s => s is not null)
            .Select(s => ToFile(s!, notes.GetValueOrDefault(s!.Id)))
            .ToList();
    }

    private static BriefStoryFile ToFile(Story story, string? note) => new()
    {
        Title = story.Title,
        Dek = story.Dek,
        Summary = story.Summary?.Summary,
        WhyItMatters = story.Summary?.WhyItMatters,
        WhoIsAffected = story.Summary?.WhoIsAffected,
        KeyPoints = story.Summary?.KeyPoints ?? [],
        PublishedAt = story.PublishedAt,
        TrustScore = story.TrustScore,
        TrustExplanation = story.Trust?.Explanation,
        HasEvidentialClaim = story.HasEvidentialClaim,
        Topics = story.Topics
            .OrderByDescending(t => t.Weight)
            .Select(t => t.Topic?.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .ToList(),
        Sources = story.Articles
            .Where(a => a.Source is not null)
            .GroupBy(a => a.Source!.Id)
            // One line per outlet, dated by its earliest piece — an outlet that
            // filed three follow-ups is still one outlet, and printing it three
            // times would read as corroboration.
            .Select(group =>
            {
                // The outlet's own earliest piece, so the link matches the date
                // printed beside it. Canonical first: it is the address with the
                // tracking parameters already stripped.
                var first = group.OrderBy(a => a.PublishedAt).First();

                return new BriefSourceRef(
                    first.Source!.Name,
                    first.Source!.IsOfficial,
                    first.PublishedAt,
                    string.IsNullOrWhiteSpace(first.CanonicalUrl) ? first.Url : first.CanonicalUrl);
            })
            .ToList(),
        Comparisons = story.Comparison?.Points
            .Select(p => new BriefComparisonPoint(p.Text, p.Kind, p.Quote, p.QuoteSource))
            .ToList() ?? [],
        Commitment = story.Commitment is null
            ? null
            : new BriefCommitmentRef(
                story.Commitment.Tier,
                story.Commitment.Event,
                story.Commitment.DateText,
                story.Commitment.Quote,
                story.Commitment.Reference,
                story.Commitment.Condition,
                story.Commitment.IsReversed),
        PersonalNote = note
    };

    internal static LearningBriefDto ToDto(LearningBrief brief, bool includePrompt) => new(
        brief.Id,
        brief.Title,
        brief.Status,
        brief.Origin,
        brief.LearningGoal,
        brief.EntryLevel,
        brief.EntryLevel is { } level ? FocusMentorPersona.LevelLabel(level) : null,
        brief.DemoIdea,
        brief.PlannerUnavailable,
        brief.CreatedAt,
        brief.GeneratedAt,
        brief.Stories
            .OrderBy(s => s.Position)
            .Select(s => new BriefStoryRefDto(
                s.StoryId,
                s.Story?.Slug ?? string.Empty,
                s.Story?.Title ?? string.Empty))
            .ToList(),
        includePrompt ? brief.Prompt : null);

    internal static IQueryable<LearningBrief> WithStories(IQueryable<LearningBrief> query) =>
        query.Include(b => b.Stories).ThenInclude(s => s.Story);
}

/// <summary>
/// "Bunu öğren" — parks a story in the learning queue. Costs nothing.
/// </summary>
public sealed record QueueStoryBriefCommand(Guid StoryId) : IRequest<LearningBriefDto>;

public sealed class QueueStoryBriefCommandHandler(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    IDateTimeProvider clock) : IRequestHandler<QueueStoryBriefCommand, LearningBriefDto>
{
    public async Task<LearningBriefDto> Handle(
        QueueStoryBriefCommand request,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            throw new UnauthorizedException();
        }

        // Pressing the button twice must not produce two lessons about the same
        // story — and must not resurrect one the reader already finished either,
        // because wanting to study it again is a new lesson.
        var existing = await BriefQueries
            .WithStories(db.LearningBriefs)
            .Where(b => b.UserId == userId &&
                        b.Status != BriefStatus.Done &&
                        b.Stories.Count == 1 &&
                        b.Stories.Any(s => s.StoryId == request.StoryId))
            .FirstOrDefaultAsync(cancellationToken);

        if (existing is not null)
        {
            return BriefQueries.ToDto(existing, includePrompt: false);
        }

        var story = await db.Stories
            .AsNoTracking()
            .Where(s => s.Id == request.StoryId)
            .Select(s => new { s.Id, s.Title, s.Slug })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw NotFoundException.For("Haber", request.StoryId);

        var brief = new LearningBrief
        {
            UserId = userId,
            Title = story.Title,
            Origin = BriefOrigin.Story,
            Status = BriefStatus.Queued,
            PersonaVersion = FocusMentorPersona.Version,
            CreatedAt = clock.UtcNow
        };

        brief.Stories.Add(new LearningBriefStory
        {
            LearningBriefId = brief.Id,
            StoryId = story.Id,
            Position = 0
        });

        db.LearningBriefs.Add(brief);
        await db.SaveChangesAsync(cancellationToken);

        return new LearningBriefDto(
            brief.Id,
            brief.Title,
            brief.Status,
            brief.Origin,
            null,
            null,
            null,
            null,
            false,
            brief.CreatedAt,
            null,
            [new BriefStoryRefDto(story.Id, story.Slug, story.Title)],
            null);
    }
}

/// <summary>
/// Queues a lesson for today's suggestion, pulling in the recent coverage behind
/// its topic so the mentor gets evidence rather than a title.
/// </summary>
public sealed record QueueDailyBriefCommand(Guid SuggestionId) : IRequest<LearningBriefDto>;

public sealed class QueueDailyBriefCommandHandler(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    IDateTimeProvider clock) : IRequestHandler<QueueDailyBriefCommand, LearningBriefDto>
{
    public async Task<LearningBriefDto> Handle(
        QueueDailyBriefCommand request,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            throw new UnauthorizedException();
        }

        var existing = await BriefQueries
            .WithStories(db.LearningBriefs)
            .Where(b => b.UserId == userId &&
                        b.Status != BriefStatus.Done &&
                        b.LearningSuggestionId == request.SuggestionId)
            .FirstOrDefaultAsync(cancellationToken);

        if (existing is not null)
        {
            return BriefQueries.ToDto(existing, includePrompt: false);
        }

        var suggestion = await db.LearningSuggestions
            .AsNoTracking()
            .FirstOrDefaultAsync(
                l => l.Id == request.SuggestionId && l.UserId == userId,
                cancellationToken)
            ?? throw NotFoundException.For("Öğrenme önerisi", request.SuggestionId);

        var storyIds = await FindBackingStoriesAsync(suggestion, cancellationToken);

        var brief = new LearningBrief
        {
            UserId = userId,
            Title = suggestion.Title,
            Origin = BriefOrigin.DailySuggestion,
            LearningSuggestionId = suggestion.Id,
            Status = BriefStatus.Queued,
            PersonaVersion = FocusMentorPersona.Version,
            CreatedAt = clock.UtcNow
        };

        var position = 0;
        foreach (var storyId in storyIds)
        {
            brief.Stories.Add(new LearningBriefStory
            {
                LearningBriefId = brief.Id,
                StoryId = storyId,
                Position = position++
            });
        }

        db.LearningBriefs.Add(brief);
        await db.SaveChangesAsync(cancellationToken);

        var saved = await BriefQueries
            .WithStories(db.LearningBriefs.AsNoTracking())
            .FirstAsync(b => b.Id == brief.Id, cancellationToken);

        return BriefQueries.ToDto(saved, includePrompt: false);
    }

    /// <summary>
    /// The daily suggestion carries at most one story id, which is thin material
    /// for a lesson. This widens it to the week's coverage of the same topic —
    /// which is where the suggestion came from in the first place.
    /// </summary>
    private async Task<List<Guid>> FindBackingStoriesAsync(
        LearningSuggestion suggestion,
        CancellationToken cancellationToken)
    {
        var ids = new List<Guid>();

        if (suggestion.SourceStoryId is { } seed)
        {
            ids.Add(seed);
        }

        if (suggestion.TopicId is { } topicId)
        {
            var since = clock.UtcNow.AddDays(-7);

            var related = await db.Stories
                .AsNoTracking()
                .Where(s => s.Status == StoryStatus.Published &&
                            s.PublishedAt >= since &&
                            s.Topics.Any(t => t.TopicId == topicId))
                .OrderByDescending(s => s.ImportanceScore)
                .Select(s => s.Id)
                .Take(BriefQueries.MaxStoriesPerBrief)
                .ToListAsync(cancellationToken);

            ids.AddRange(related);
        }

        return ids.Distinct().Take(BriefQueries.MaxStoriesPerBrief).ToList();
    }
}

/// <summary>
/// Composes the case file, asks the planner for the questions, and stores the
/// result. This is the only command here that spends a model call.
/// </summary>
public sealed record GenerateBriefCommand(Guid BriefId) : IRequest<LearningBriefDto>;

public sealed class GenerateBriefCommandHandler(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    IContentAiService ai,
    IDateTimeProvider clock) : IRequestHandler<GenerateBriefCommand, LearningBriefDto>
{
    public async Task<LearningBriefDto> Handle(
        GenerateBriefCommand request,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            throw new UnauthorizedException();
        }

        var brief = await BriefQueries
            .WithStories(db.LearningBriefs)
            .FirstOrDefaultAsync(
                b => b.Id == request.BriefId && b.UserId == userId,
                cancellationToken)
            ?? throw NotFoundException.For("Ders brifi", request.BriefId);

        // Already paid for. Regenerating would spend a second call and hand back a
        // different prompt than the one a lesson in progress was started from.
        if (brief.Prompt is not null)
        {
            return BriefQueries.ToDto(brief, includePrompt: true);
        }

        var minutes = await db.UserProfiles
            .AsNoTracking()
            .Where(p => p.UserId == userId)
            .Select(p => (int?)p.DailyLearningMinutes)
            .FirstOrDefaultAsync(cancellationToken) ?? 15;

        var rationale = brief.LearningSuggestionId is { } suggestionId
            ? await db.LearningSuggestions
                .AsNoTracking()
                .Where(l => l.Id == suggestionId)
                .Select(l => l.Rationale)
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        var storyFiles = await BriefQueries.LoadStoryFilesAsync(
            db,
            brief.Stories.OrderBy(s => s.Position).Select(s => s.StoryId).ToList(),
            userId,
            cancellationToken);

        var caseFile = new BriefCaseFile
        {
            Title = brief.Title,
            Stories = storyFiles,
            Minutes = minutes,
            DailyRationale = rationale
        };

        // The planner is shown the same text the mentor will read, so a question it
        // writes is always answerable from the file it was written against.
        var plan = await ai.PlanBriefAsync(
            BriefComposer.Compose(caseFile),
            minutes,
            cancellationToken);

        brief.Prompt = BriefComposer.Compose(caseFile, plan);
        brief.LearningGoal = plan?.LearningGoal;
        brief.EntryLevel = plan?.EntryLevel;
        brief.DemoIdea = plan?.DemoIdea;
        brief.PlannerUnavailable = plan is null;
        brief.Provider = plan?.Provider;
        brief.Model = plan?.Model;
        brief.PersonaVersion = FocusMentorPersona.Version;
        brief.GeneratedAt = clock.UtcNow;
        brief.Status = BriefStatus.Generated;
        brief.UpdatedAt = clock.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return BriefQueries.ToDto(brief, includePrompt: true);
    }
}

public sealed record GetBriefsQuery(int Take = 50) : IRequest<IReadOnlyList<LearningBriefDto>>;

public sealed class GetBriefsQueryHandler(
    IApplicationDbContext db,
    ICurrentUser currentUser) : IRequestHandler<GetBriefsQuery, IReadOnlyList<LearningBriefDto>>
{
    public async Task<IReadOnlyList<LearningBriefDto>> Handle(
        GetBriefsQuery request,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            throw new UnauthorizedException();
        }

        var briefs = await BriefQueries
            .WithStories(db.LearningBriefs.AsNoTracking())
            .Where(b => b.UserId == userId)
            .OrderByDescending(b => b.CreatedAt)
            .Take(Math.Clamp(request.Take, 1, 200))
            .ToListAsync(cancellationToken);

        // The prompt is several kilobytes; the list never shows it.
        return briefs.Select(b => BriefQueries.ToDto(b, includePrompt: false)).ToList();
    }
}

/// <summary>
/// The Öğren tab's list: stories taken into learning, as feed cards.
/// </summary>
/// <remarks>
/// Story-origin briefs only. The daily suggestion has its own block at the top of
/// the tab, and the five stories it pulls in as backing evidence would read as
/// five separate things to study if they were listed here as well.
/// </remarks>
public sealed record GetLearningStoriesQuery(int Take = 50) : IRequest<IReadOnlyList<LearningStoryDto>>;

public sealed class GetLearningStoriesQueryHandler(
    IApplicationDbContext db,
    ICurrentUser currentUser) : IRequestHandler<GetLearningStoriesQuery, IReadOnlyList<LearningStoryDto>>
{
    public async Task<IReadOnlyList<LearningStoryDto>> Handle(
        GetLearningStoriesQuery request,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            throw new UnauthorizedException();
        }

        var briefs = await db.LearningBriefs
            .AsNoTracking()
            .Where(b => b.UserId == userId &&
                        b.Origin == BriefOrigin.Story &&
                        b.Status != BriefStatus.Done)
            .OrderByDescending(b => b.CreatedAt)
            .Take(Math.Clamp(request.Take, 1, 200))
            .Select(b => new
            {
                b.Id,
                b.Status,
                b.CreatedAt,
                StoryIds = b.Stories.OrderBy(s => s.Position).Select(s => s.StoryId).ToList()
            })
            .ToListAsync(cancellationToken);

        var storyIds = briefs.SelectMany(b => b.StoryIds).Distinct().ToList();
        if (storyIds.Count == 0)
        {
            return [];
        }

        // The same projection the feed and Keşfet use, so a card cannot drift into
        // looking different depending on which tab drew it.
        var cards = await db.Stories
            .AsNoTracking()
            .Where(s => storyIds.Contains(s.Id))
            .Select(StoryProjections.ToCard())
            .ToListAsync(cancellationToken);

        var byId = cards.ToDictionary(c => c.Id);

        return briefs
            .SelectMany(b => b.StoryIds.Select(id => (Brief: b, StoryId: id)))
            .Where(row => byId.ContainsKey(row.StoryId))
            .Select(row => new LearningStoryDto(byId[row.StoryId], row.Brief.Id, row.Brief.Status))
            .ToList();
    }
}

public sealed record GetBriefQuery(Guid BriefId) : IRequest<LearningBriefDto>;

public sealed class GetBriefQueryHandler(
    IApplicationDbContext db,
    ICurrentUser currentUser) : IRequestHandler<GetBriefQuery, LearningBriefDto>
{
    public async Task<LearningBriefDto> Handle(GetBriefQuery request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            throw new UnauthorizedException();
        }

        var brief = await BriefQueries
            .WithStories(db.LearningBriefs.AsNoTracking())
            .FirstOrDefaultAsync(
                b => b.Id == request.BriefId && b.UserId == userId,
                cancellationToken)
            ?? throw NotFoundException.For("Ders brifi", request.BriefId);

        return BriefQueries.ToDto(brief, includePrompt: true);
    }
}

public sealed record UpdateBriefStatusCommand(Guid BriefId, BriefStatus Status) : IRequest<Unit>;

public sealed class UpdateBriefStatusCommandHandler(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    IDateTimeProvider clock) : IRequestHandler<UpdateBriefStatusCommand, Unit>
{
    public async Task<Unit> Handle(UpdateBriefStatusCommand request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            throw new UnauthorizedException();
        }

        var brief = await db.LearningBriefs
            .FirstOrDefaultAsync(b => b.Id == request.BriefId && b.UserId == userId, cancellationToken)
            ?? throw NotFoundException.For("Ders brifi", request.BriefId);

        // Queued means "no prompt yet", which is a fact about the row rather than a
        // state a reader can choose to return to.
        if (request.Status != BriefStatus.Queued && brief.Prompt is null)
        {
            throw new ConflictException("Bu brif için henüz prompt üretilmedi.");
        }

        var wasDone = brief.Status == BriefStatus.Done;
        brief.Status = request.Status;
        brief.UpdatedAt = clock.UtcNow;

        if (request.Status == BriefStatus.Done)
        {
            brief.CompletedAt = clock.UtcNow;

            if (!wasDone)
            {
                var streak = await db.UserStreaks
                    .FirstOrDefaultAsync(s => s.UserId == userId, cancellationToken);

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

public sealed record DeleteBriefCommand(Guid BriefId) : IRequest<Unit>;

public sealed class DeleteBriefCommandHandler(
    IApplicationDbContext db,
    ICurrentUser currentUser) : IRequestHandler<DeleteBriefCommand, Unit>
{
    public async Task<Unit> Handle(DeleteBriefCommand request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            throw new UnauthorizedException();
        }

        var brief = await db.LearningBriefs
            .FirstOrDefaultAsync(b => b.Id == request.BriefId && b.UserId == userId, cancellationToken);

        if (brief is null)
        {
            return Unit.Value;
        }

        db.LearningBriefs.Remove(brief);
        await db.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
