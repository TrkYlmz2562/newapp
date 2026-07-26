using FluentValidation;
using FocusAI.Application.Common.Exceptions;
using FocusAI.Application.Common.Interfaces;
using FocusAI.Application.Dtos;
using MediatR;
using Microsoft.EntityFrameworkCore;
using RefreshTokenEntity = FocusAI.Domain.Entities.Users.RefreshToken;

namespace FocusAI.Application.Features.Auth;

public sealed record RefreshTokenCommand(string RefreshToken) : IRequest<AuthResultDto>;

public sealed class RefreshTokenCommandValidator : AbstractValidator<RefreshTokenCommand>
{
    public RefreshTokenCommandValidator() => RuleFor(x => x.RefreshToken).NotEmpty();
}

public sealed class RefreshTokenCommandHandler(
    IApplicationDbContext db,
    ITokenService tokenService,
    IDateTimeProvider clock) : IRequestHandler<RefreshTokenCommand, AuthResultDto>
{
    public async Task<AuthResultDto> Handle(RefreshTokenCommand request, CancellationToken cancellationToken)
    {
        var hash = tokenService.HashRefreshToken(request.RefreshToken);
        var now = clock.UtcNow;

        var stored = await db.RefreshTokens
            .Include(t => t.User)
            .ThenInclude(u => u!.Profile)
            .FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);

        if (stored is null || stored.User is null)
        {
            throw new UnauthorizedException("Oturum bulunamadı, lütfen tekrar giriş yapın.");
        }

        if (!stored.IsActive(now))
        {
            // A presented-but-inactive token means either an expiry or a replay.
            // Revoking the whole family is the cheap, safe response to replay.
            var family = await db.RefreshTokens
                .Where(t => t.UserId == stored.UserId && t.RevokedAt == null)
                .ToListAsync(cancellationToken);

            foreach (var token in family)
            {
                token.RevokedAt = now;
            }

            await db.SaveChangesAsync(cancellationToken);
            throw new UnauthorizedException("Oturum süresi doldu, lütfen tekrar giriş yapın.");
        }

        var user = stored.User;
        var tokens = tokenService.Issue(user.Id, user.Email, user.Roles);

        var replacement = new RefreshTokenEntity
        {
            UserId = user.Id,
            TokenHash = tokenService.HashRefreshToken(tokens.RefreshToken),
            ExpiresAt = tokens.RefreshTokenExpiresAt,
            CreatedAt = now
        };

        stored.RevokedAt = now;
        stored.ReplacedByTokenId = replacement.Id;
        db.RefreshTokens.Add(replacement);

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
                user.Profile?.OnboardingCompleted ?? false));
    }
}

public sealed record LogoutCommand(string RefreshToken) : IRequest<Unit>;

public sealed class LogoutCommandHandler(
    IApplicationDbContext db,
    ITokenService tokenService,
    IDateTimeProvider clock) : IRequestHandler<LogoutCommand, Unit>
{
    public async Task<Unit> Handle(LogoutCommand request, CancellationToken cancellationToken)
    {
        var hash = tokenService.HashRefreshToken(request.RefreshToken);
        var stored = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);

        // Logging out an already-invalid token is not an error worth surfacing.
        if (stored is { RevokedAt: null })
        {
            stored.RevokedAt = clock.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }

        return Unit.Value;
    }
}
