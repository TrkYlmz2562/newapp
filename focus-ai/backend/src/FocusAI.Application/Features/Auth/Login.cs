using FluentValidation;
using FocusAI.Application.Common.Exceptions;
using FocusAI.Application.Common.Interfaces;
using FocusAI.Application.Dtos;
using FocusAI.Domain.Entities.Users;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FocusAI.Application.Features.Auth;

public sealed record LoginCommand(string Email, string Password) : IRequest<AuthResultDto>;

public sealed class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password).NotEmpty();
    }
}

public sealed class LoginCommandHandler(
    IApplicationDbContext db,
    IPasswordHasher passwordHasher,
    ITokenService tokenService,
    IDateTimeProvider clock) : IRequestHandler<LoginCommand, AuthResultDto>
{
    public async Task<AuthResultDto> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        var user = await db.Users
            .Include(u => u.Profile)
            .FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail, cancellationToken);

        // Same message for "no such user" and "wrong password" so the endpoint
        // cannot be used to enumerate registered addresses.
        if (user is null || !passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            throw new UnauthorizedException("E-posta veya şifre hatalı.");
        }

        if (!user.IsActive)
        {
            throw new ForbiddenException("Hesabınız devre dışı bırakılmış.");
        }

        var now = clock.UtcNow;
        user.LastLoginAt = now;

        var tokens = tokenService.Issue(user.Id, user.Email, user.Roles);
        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = tokenService.HashRefreshToken(tokens.RefreshToken),
            ExpiresAt = tokens.RefreshTokenExpiresAt,
            CreatedAt = now
        });

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
