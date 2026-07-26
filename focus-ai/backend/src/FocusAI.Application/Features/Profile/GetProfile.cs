using FocusAI.Application.Common.Exceptions;
using FocusAI.Application.Common.Interfaces;
using FocusAI.Application.Dtos;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FocusAI.Application.Features.Profile;

public sealed record GetProfileQuery : IRequest<ProfileDto>;

public sealed class GetProfileQueryHandler(
    IApplicationDbContext db,
    ICurrentUser currentUser) : IRequestHandler<GetProfileQuery, ProfileDto>
{
    public async Task<ProfileDto> Handle(GetProfileQuery request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            throw new UnauthorizedException();
        }

        var user = await db.Users
            .AsNoTracking()
            .Include(u => u.Profile)!.ThenInclude(p => p!.Interests).ThenInclude(i => i.Topic)
            .Include(u => u.Profile)!.ThenInclude(p => p!.MutedTopics).ThenInclude(m => m.Topic)
            .Include(u => u.Profile)!.ThenInclude(p => p!.FavoriteSources).ThenInclude(f => f.Source)
            .Include(u => u.NotificationSettings)
            .Include(u => u.Streak)!.ThenInclude(s => s!.Badges).ThenInclude(b => b.Badge)
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user is null)
        {
            throw NotFoundException.For("Kullanıcı", userId);
        }

        var profile = user.Profile;
        var notifications = user.NotificationSettings;
        var streak = user.Streak;

        return new ProfileDto(
            user.Id,
            user.DisplayName,
            user.Email,
            profile?.Headline,
            profile?.ExperienceLevel ?? Domain.Enums.ExperienceLevel.Mid,
            profile?.DailyDigestHour ?? 8,
            profile?.DailyStoryCount ?? 10,
            profile?.DailyLearningMinutes ?? 15,
            profile?.OnboardingCompleted ?? false,
            user.Plan,
            profile?.Interests
                .Where(i => i.Topic is not null)
                .OrderByDescending(i => i.Weight)
                .Select(i => new InterestDto(i.TopicId, i.Topic!.Name, i.Topic.Slug, i.Weight, i.IsExplicit))
                .ToList() ?? [],
            profile?.MutedTopics
                .Where(m => m.Topic is not null)
                .Select(m => new TopicDto(m.TopicId, m.Topic!.Name, m.Topic.Slug, m.Topic.Kind))
                .ToList() ?? [],
            profile?.FavoriteSources
                .Where(f => f.Source is not null)
                .Select(f => new FavoriteSourceDto(f.SourceId, f.Source!.Name, f.Source.Slug, f.Source.IconUrl))
                .ToList() ?? [],
            new NotificationSettingsDto(
                notifications?.MorningDigest ?? true,
                notifications?.EveningDigest ?? false,
                notifications?.BigNewsOnly ?? true,
                notifications?.WeeklyDigest ?? true,
                notifications?.BigNewsThreshold ?? 85,
                notifications?.QuietHoursStart ?? 22,
                notifications?.QuietHoursEnd ?? 7),
            new StreakDto(
                streak?.CurrentStreak ?? 0,
                streak?.LongestStreak ?? 0,
                streak?.WeeklyGoalDays ?? 5,
                streak?.CompletedLearnings ?? 0,
                streak?.TotalStoriesRead ?? 0,
                streak?.LastActiveDate,
                streak?.Badges
                    .Where(b => b.Badge is not null)
                    .Select(b => new BadgeDto(
                        b.Badge!.Slug,
                        b.Badge.Name,
                        b.Badge.Description,
                        b.Badge.Emoji,
                        b.AwardedAt))
                    .ToList() ?? []));
    }
}
