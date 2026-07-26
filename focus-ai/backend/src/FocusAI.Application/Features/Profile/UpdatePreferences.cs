using FluentValidation;
using FocusAI.Application.Common.Exceptions;
using FocusAI.Application.Common.Interfaces;
using FocusAI.Domain.Common;
using FocusAI.Domain.Entities.Users;
using FocusAI.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FocusAI.Application.Features.Profile;

public sealed record UpdateProfileCommand(
    string? DisplayName,
    string? Headline,
    ExperienceLevel? ExperienceLevel,
    int? DailyDigestHour,
    int? DailyStoryCount,
    int? DailyLearningMinutes,
    string? TimeZone,
    string? Locale) : IRequest<Unit>;

public sealed class UpdateProfileCommandValidator : AbstractValidator<UpdateProfileCommand>
{
    public UpdateProfileCommandValidator()
    {
        RuleFor(x => x.DisplayName).MaximumLength(80);
        RuleFor(x => x.Headline).MaximumLength(200);
        RuleFor(x => x.DailyDigestHour).InclusiveBetween(0, 23).When(x => x.DailyDigestHour.HasValue);
        RuleFor(x => x.DailyStoryCount).InclusiveBetween(3, 30).When(x => x.DailyStoryCount.HasValue);
        RuleFor(x => x.DailyLearningMinutes)
            .InclusiveBetween(5, 120)
            .When(x => x.DailyLearningMinutes.HasValue);
    }
}

public sealed class UpdateProfileCommandHandler(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    IDateTimeProvider clock) : IRequestHandler<UpdateProfileCommand, Unit>
{
    public async Task<Unit> Handle(UpdateProfileCommand request, CancellationToken cancellationToken)
    {
        var user = await LoadUserWithProfile(db, currentUser, cancellationToken);
        var profile = user.Profile!;

        if (!string.IsNullOrWhiteSpace(request.DisplayName))
        {
            user.DisplayName = request.DisplayName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(request.TimeZone))
        {
            user.TimeZone = request.TimeZone;
        }

        if (!string.IsNullOrWhiteSpace(request.Locale))
        {
            user.Locale = request.Locale;
        }

        profile.Headline = request.Headline?.Trim() ?? profile.Headline;
        profile.ExperienceLevel = request.ExperienceLevel ?? profile.ExperienceLevel;
        profile.DailyDigestHour = request.DailyDigestHour ?? profile.DailyDigestHour;
        profile.DailyStoryCount = request.DailyStoryCount ?? profile.DailyStoryCount;
        profile.DailyLearningMinutes = request.DailyLearningMinutes ?? profile.DailyLearningMinutes;
        profile.UpdatedAt = clock.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }

    internal static async Task<User> LoadUserWithProfile(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            throw new UnauthorizedException();
        }

        var user = await db.Users
            .Include(u => u.Profile)
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user is null)
        {
            throw NotFoundException.For("Kullanıcı", userId);
        }

        // Older accounts predate the profile table; create lazily rather than fail.
        user.Profile ??= new UserProfile { UserId = user.Id };
        return user;
    }
}

/// <summary>Replaces the whole interest set — the onboarding screen sends its full selection.</summary>
public sealed record UpdateInterestsCommand(IReadOnlyList<string> TopicSlugs) : IRequest<Unit>;

public sealed class UpdateInterestsCommandValidator : AbstractValidator<UpdateInterestsCommand>
{
    public UpdateInterestsCommandValidator()
    {
        RuleFor(x => x.TopicSlugs).NotNull();
        RuleFor(x => x.TopicSlugs.Count)
            .LessThanOrEqualTo(FieldLimits.MaxInterests)
            .WithMessage($"En fazla {FieldLimits.MaxInterests} ilgi alanı seçilebilir.");
    }
}

public sealed class UpdateInterestsCommandHandler(
    IApplicationDbContext db,
    ICurrentUser currentUser) : IRequestHandler<UpdateInterestsCommand, Unit>
{
    public async Task<Unit> Handle(UpdateInterestsCommand request, CancellationToken cancellationToken)
    {
        var user = await UpdateProfileCommandHandler.LoadUserWithProfile(db, currentUser, cancellationToken);
        var profile = user.Profile!;

        var slugs = request.TopicSlugs
            .Select(s => s.Trim().ToLowerInvariant())
            .Where(s => s.Length > 0)
            .Distinct()
            .ToArray();

        var topicIds = await db.Topics
            .Where(t => slugs.Contains(t.Slug))
            .Select(t => t.Id)
            .ToListAsync(cancellationToken);

        var existing = await db.UserInterests
            .Where(i => i.UserProfileId == profile.Id)
            .ToListAsync(cancellationToken);

        var desired = topicIds.ToHashSet();

        // Behaviour-inferred interests are preserved: the user never chose them,
        // so an explicit edit of the declared set should not silently delete them.
        var removable = existing.Where(i => i.IsExplicit && !desired.Contains(i.TopicId)).ToList();
        db.UserInterests.RemoveRange(removable);

        var present = existing.Select(i => i.TopicId).ToHashSet();
        foreach (var topicId in desired.Where(id => !present.Contains(id)))
        {
            db.UserInterests.Add(new UserInterest
            {
                UserProfileId = profile.Id,
                TopicId = topicId,
                Weight = 1.0,
                IsExplicit = true
            });
        }

        foreach (var interest in existing.Where(i => desired.Contains(i.TopicId)))
        {
            interest.IsExplicit = true;
            interest.Weight = Math.Max(interest.Weight, 1.0);
        }

        profile.OnboardingCompleted = true;

        await db.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

public sealed record ToggleMutedTopicCommand(string TopicSlug) : IRequest<bool>;

public sealed class ToggleMutedTopicCommandHandler(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    IDateTimeProvider clock) : IRequestHandler<ToggleMutedTopicCommand, bool>
{
    public async Task<bool> Handle(ToggleMutedTopicCommand request, CancellationToken cancellationToken)
    {
        var user = await UpdateProfileCommandHandler.LoadUserWithProfile(db, currentUser, cancellationToken);
        var profile = user.Profile!;
        var slug = request.TopicSlug.Trim().ToLowerInvariant();

        var topic = await db.Topics.FirstOrDefaultAsync(t => t.Slug == slug, cancellationToken)
                    ?? throw NotFoundException.For("Konu", request.TopicSlug);

        var existing = await db.UserMutedTopics
            .FirstOrDefaultAsync(m => m.UserProfileId == profile.Id && m.TopicId == topic.Id, cancellationToken);

        if (existing is not null)
        {
            db.UserMutedTopics.Remove(existing);
            await db.SaveChangesAsync(cancellationToken);
            return false;
        }

        db.UserMutedTopics.Add(new UserMutedTopic
        {
            UserProfileId = profile.Id,
            TopicId = topic.Id,
            MutedAt = clock.UtcNow
        });

        // Muting also drops the matching interest; keeping both is contradictory.
        var interest = await db.UserInterests
            .FirstOrDefaultAsync(i => i.UserProfileId == profile.Id && i.TopicId == topic.Id, cancellationToken);

        if (interest is not null)
        {
            db.UserInterests.Remove(interest);
        }

        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}

public sealed record ToggleFavoriteSourceCommand(Guid SourceId) : IRequest<bool>;

public sealed class ToggleFavoriteSourceCommandHandler(
    IApplicationDbContext db,
    ICurrentUser currentUser) : IRequestHandler<ToggleFavoriteSourceCommand, bool>
{
    public async Task<bool> Handle(ToggleFavoriteSourceCommand request, CancellationToken cancellationToken)
    {
        var user = await UpdateProfileCommandHandler.LoadUserWithProfile(db, currentUser, cancellationToken);
        var profile = user.Profile!;

        var existing = await db.UserFavoriteSources
            .FirstOrDefaultAsync(
                f => f.UserProfileId == profile.Id && f.SourceId == request.SourceId,
                cancellationToken);

        if (existing is not null)
        {
            db.UserFavoriteSources.Remove(existing);
            await db.SaveChangesAsync(cancellationToken);
            return false;
        }

        if (!await db.Sources.AnyAsync(s => s.Id == request.SourceId, cancellationToken))
        {
            throw NotFoundException.For("Kaynak", request.SourceId);
        }

        db.UserFavoriteSources.Add(new UserFavoriteSource
        {
            UserProfileId = profile.Id,
            SourceId = request.SourceId
        });

        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}

public sealed record UpdateNotificationSettingsCommand(
    bool? MorningDigest,
    bool? EveningDigest,
    bool? BigNewsOnly,
    bool? WeeklyDigest,
    int? BigNewsThreshold,
    int? QuietHoursStart,
    int? QuietHoursEnd) : IRequest<Unit>;

public sealed class UpdateNotificationSettingsCommandValidator
    : AbstractValidator<UpdateNotificationSettingsCommand>
{
    public UpdateNotificationSettingsCommandValidator()
    {
        RuleFor(x => x.BigNewsThreshold)
            .InclusiveBetween(50, 100)
            .When(x => x.BigNewsThreshold.HasValue);
        RuleFor(x => x.QuietHoursStart).InclusiveBetween(0, 23).When(x => x.QuietHoursStart.HasValue);
        RuleFor(x => x.QuietHoursEnd).InclusiveBetween(0, 23).When(x => x.QuietHoursEnd.HasValue);
    }
}

public sealed class UpdateNotificationSettingsCommandHandler(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    IDateTimeProvider clock) : IRequestHandler<UpdateNotificationSettingsCommand, Unit>
{
    public async Task<Unit> Handle(
        UpdateNotificationSettingsCommand request,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            throw new UnauthorizedException();
        }

        var settings = await db.NotificationSettings
            .FirstOrDefaultAsync(n => n.UserId == userId, cancellationToken);

        if (settings is null)
        {
            settings = new NotificationSettings { UserId = userId, CreatedAt = clock.UtcNow };
            db.NotificationSettings.Add(settings);
        }

        settings.MorningDigest = request.MorningDigest ?? settings.MorningDigest;
        settings.EveningDigest = request.EveningDigest ?? settings.EveningDigest;
        settings.BigNewsOnly = request.BigNewsOnly ?? settings.BigNewsOnly;
        settings.WeeklyDigest = request.WeeklyDigest ?? settings.WeeklyDigest;
        settings.BigNewsThreshold = request.BigNewsThreshold ?? settings.BigNewsThreshold;
        settings.QuietHoursStart = request.QuietHoursStart ?? settings.QuietHoursStart;
        settings.QuietHoursEnd = request.QuietHoursEnd ?? settings.QuietHoursEnd;
        settings.UpdatedAt = clock.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
