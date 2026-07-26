using FluentValidation;
using FocusAI.Application.Common.Exceptions;
using FocusAI.Application.Common.Interfaces;
using FocusAI.Application.Dtos;
using FocusAI.Domain.Entities.Content;
using FocusAI.Domain.Entities.Gamification;
using FocusAI.Domain.Entities.Users;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FocusAI.Application.Features.Auth;

public sealed record RegisterCommand(
    string Email,
    string Password,
    string DisplayName,
    string? TimeZone,
    string? Locale,
    IReadOnlyList<string>? InterestSlugs) : IRequest<AuthResultDto>;

public sealed class RegisterCommandValidator : AbstractValidator<RegisterCommand>
{
    public RegisterCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.Password)
            .NotEmpty()
            .MinimumLength(8).WithMessage("Şifre en az 8 karakter olmalı.")
            .MaximumLength(128);
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(80);
        RuleFor(x => x.InterestSlugs)
            .Must(slugs => slugs is null || slugs.Count <= 40)
            .WithMessage("En fazla 40 ilgi alanı seçilebilir.");
    }
}

public sealed class RegisterCommandHandler(
    IApplicationDbContext db,
    IPasswordHasher passwordHasher,
    ITokenService tokenService,
    IDateTimeProvider clock) : IRequestHandler<RegisterCommand, AuthResultDto>
{
    public async Task<AuthResultDto> Handle(RegisterCommand request, CancellationToken cancellationToken)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        if (await db.Users.AnyAsync(u => u.NormalizedEmail == normalizedEmail, cancellationToken))
        {
            throw new ConflictException("Bu e-posta adresi zaten kayıtlı.");
        }

        var now = clock.UtcNow;
        var user = new User
        {
            Email = request.Email.Trim(),
            NormalizedEmail = normalizedEmail,
            DisplayName = request.DisplayName.Trim(),
            PasswordHash = passwordHasher.Hash(request.Password),
            TimeZone = string.IsNullOrWhiteSpace(request.TimeZone) ? "Europe/Istanbul" : request.TimeZone,
            Locale = string.IsNullOrWhiteSpace(request.Locale) ? "tr" : request.Locale,
            CreatedAt = now
        };

        var profile = new UserProfile
        {
            UserId = user.Id,
            CreatedAt = now,
            OnboardingCompleted = request.InterestSlugs is { Count: > 0 }
        };

        // Resolve declared interests up front so the very first feed is already
        // personalised — a cold start with zero signal is the worst first impression.
        if (request.InterestSlugs is { Count: > 0 })
        {
            var slugs = request.InterestSlugs
                .Select(s => s.Trim().ToLowerInvariant())
                .Distinct()
                .ToArray();

            var topics = await db.Topics
                .Where(t => slugs.Contains(t.Slug))
                .Select(t => t.Id)
                .ToListAsync(cancellationToken);

            foreach (var topicId in topics)
            {
                profile.Interests.Add(new UserInterest
                {
                    UserProfileId = profile.Id,
                    TopicId = topicId,
                    Weight = 1.0,
                    IsExplicit = true
                });
            }
        }

        user.Profile = profile;
        user.NotificationSettings = new NotificationSettings { UserId = user.Id, CreatedAt = now };
        user.Streak = new UserStreak { UserId = user.Id, CreatedAt = now };

        var tokens = tokenService.Issue(user.Id, user.Email, user.Roles);
        user.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = tokenService.HashRefreshToken(tokens.RefreshToken),
            ExpiresAt = tokens.RefreshTokenExpiresAt,
            CreatedAt = now
        });

        db.Users.Add(user);
        await db.SaveChangesAsync(cancellationToken);

        return new AuthResultDto(
            tokens.AccessToken,
            tokens.RefreshToken,
            tokens.AccessTokenExpiresAt,
            new UserDto(
                user.Id,
                user.Email,
                user.DisplayName,
                user.Plan,
                user.TimeZone,
                user.Locale,
                user.Roles,
                profile.OnboardingCompleted));
    }
}
