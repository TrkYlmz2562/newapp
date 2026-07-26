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
        var group = app.MapGroup("/api/auth")
            .WithTags("Auth")
            // Auth is the obvious credential-stuffing target, so it gets its own
            // stricter limiter rather than the shared default.
            .RequireRateLimiting("auth");

        group.MapPost("/register", async (RegisterCommand command, ISender sender, CancellationToken ct) =>
                Results.Ok(await sender.Send(command, ct)))
            .WithSummary("Yeni hesap oluşturur ve oturum açar.")
            .Produces<AuthResultDto>()
            .AllowAnonymous();

        group.MapPost("/login", async (LoginCommand command, ISender sender, CancellationToken ct) =>
                Results.Ok(await sender.Send(command, ct)))
            .WithSummary("E-posta ve şifre ile oturum açar.")
            .Produces<AuthResultDto>()
            .AllowAnonymous();

        group.MapPost("/refresh", async (RefreshTokenCommand command, ISender sender, CancellationToken ct) =>
                Results.Ok(await sender.Send(command, ct)))
            .WithSummary("Refresh token ile yeni erişim tokenı alır.")
            .Produces<AuthResultDto>()
            .AllowAnonymous();

        group.MapPost("/logout", async (LogoutCommand command, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(command, ct);
                return Results.NoContent();
            })
            .WithSummary("Refresh tokenı iptal eder.")
            .AllowAnonymous();

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
            .RequireAuthorization();

        return app;
    }
}
