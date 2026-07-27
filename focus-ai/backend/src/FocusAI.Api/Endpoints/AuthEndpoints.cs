using FocusAI.Application.Common.Exceptions;
using FocusAI.Application.Common.Interfaces;
using FocusAI.Application.Dtos;
using FocusAI.Application.Features.Auth;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FocusAI.Api.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        /*
         * No limiter on the group.
         *
         * These endpoints look alike and are not: two of them accept a guess at a
         * credential, and three of them are what a signed-in browser does by
         * itself. Sharing one budget between those meant a reader could be locked
         * out of their own account by browsing — see the "session" limiter.
         */
        var group = app.MapGroup("/api/auth").WithTags("Auth");

        group.MapPost("/register", async (RegisterCommand command, ISender sender, CancellationToken ct) =>
                Results.Ok(await sender.Send(command, ct)))
            .WithSummary("Yeni hesap oluşturur ve oturum açar.")
            .Produces<AuthResultDto>()
            .AllowAnonymous()
            .RequireRateLimiting("auth");

        group.MapPost("/login", async (LoginCommand command, ISender sender, CancellationToken ct) =>
                Results.Ok(await sender.Send(command, ct)))
            .WithSummary("E-posta ve şifre ile oturum açar.")
            .Produces<AuthResultDto>()
            .AllowAnonymous()
            .RequireRateLimiting("auth");

        // Not a guessing game: the token is rotating and single-use, and reusing a
        // spent one revokes the whole session on the spot. Throttling it as if it
        // were a password field only ever hurts the legitimate holder.
        group.MapPost("/refresh", async (RefreshTokenCommand command, ISender sender, CancellationToken ct) =>
                Results.Ok(await sender.Send(command, ct)))
            .WithSummary("Refresh token ile yeni erişim tokenı alır.")
            .Produces<AuthResultDto>()
            .AllowAnonymous()
            .RequireRateLimiting("session");

        group.MapPost("/logout", async (LogoutCommand command, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(command, ct);
                return Results.NoContent();
            })
            .WithSummary("Refresh tokenı iptal eder.")
            .AllowAnonymous()
            .RequireRateLimiting("session");

        group.MapGet("/me", async (
                ICurrentUser currentUser,
                IApplicationDbContext db,
                CancellationToken ct) =>
            {
                if (currentUser.UserId is not { } userId)
                {
                    throw new UnauthorizedException();
                }

                var user = await db.Users
                    .AsNoTracking()
                    .Where(u => u.Id == userId)
                    .Select(u => new UserDto(
                        u.Id,
                        u.Email,
                        u.DisplayName,
                        u.Plan,
                        u.TimeZone,
                        u.Locale,
                        u.Roles,
                        u.Profile != null && u.Profile.OnboardingCompleted))
                    .FirstOrDefaultAsync(ct);

                return user is null ? Results.NotFound() : Results.Ok(user);
            })
            .WithSummary("Oturum açmış kullanıcıyı döndürür.")
            .Produces<UserDto>()
            .RequireAuthorization()
            // The single most-called endpoint in the app: every page load asks it
            // who the reader is. It was the main thing draining the login budget.
            .RequireRateLimiting("session");

        return app;
    }
}
