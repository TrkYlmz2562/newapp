using FocusAI.Application.Common.Models;
using FocusAI.Application.Dtos;
using FocusAI.Application.Features.Bookmarks;
using FocusAI.Application.Features.Digests;
using FocusAI.Application.Features.Learning;
using FocusAI.Application.Features.Profile;
using FocusAI.Application.Features.Trends;
using FocusAI.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace FocusAI.Api.Endpoints;

public static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        MapDigest(app);
        MapBookmarks(app);
        MapProfile(app);
        MapLearning(app);
        MapTrends(app);

        return app;
    }

    private static void MapDigest(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/digest").WithTags("Digest");

        group.MapGet("/daily", async ([FromQuery] DateOnly? date, ISender sender, CancellationToken ct) =>
            {
                var digest = await sender.Send(new GetDigestQuery(DigestPeriod.Daily, date), ct);
                return digest is null ? Results.NoContent() : Results.Ok(digest);
            })
            .WithSummary("Bugünün özeti — günün bilmen gereken konuları.")
            .Produces<DigestDto>()
            .AllowAnonymous();

        group.MapGet("/weekly", async ([FromQuery] DateOnly? date, ISender sender, CancellationToken ct) =>
            {
                var digest = await sender.Send(new GetDigestQuery(DigestPeriod.Weekly, date), ct);
                return digest is null ? Results.NoContent() : Results.Ok(digest);
            })
            .WithSummary("Haftanın en önemli gelişmeleri.")
            .Produces<DigestDto>()
            .AllowAnonymous();
    }

    private static void MapBookmarks(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/bookmarks")
            .WithTags("Bookmarks")
            .RequireAuthorization();

        group.MapGet("/", async (
                [FromQuery] int? page,
                [FromQuery] int? pageSize,
                [FromQuery] string? tag,
                ISender sender,
                CancellationToken ct) =>
                Results.Ok(await sender.Send(new GetBookmarksQuery(page ?? 1, pageSize ?? 20, tag), ct)))
            .WithSummary("Kaydedilen haberler.")
            .Produces<PagedResult<BookmarkDto>>();

        group.MapPost("/toggle", async (ToggleBookmarkCommand command, ISender sender, CancellationToken ct) =>
                Results.Ok(new { saved = await sender.Send(command, ct) }))
            .WithSummary("Haberi kaydeder veya kaydı kaldırır.");
    }

    private static void MapProfile(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/profile")
            .WithTags("Profile")
            .RequireAuthorization();

        group.MapGet("/", async (ISender sender, CancellationToken ct) =>
                Results.Ok(await sender.Send(new GetProfileQuery(), ct)))
            .WithSummary("Profil, ilgi alanları, bildirim ayarları ve seri bilgisi.")
            .Produces<ProfileDto>();

        group.MapPut("/", async (UpdateProfileCommand command, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(command, ct);
                return Results.NoContent();
            })
            .WithSummary("Profil bilgilerini günceller.");

        group.MapPut("/interests", async (UpdateInterestsCommand command, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(command, ct);
                return Results.NoContent();
            })
            .WithSummary("İlgi alanlarını topluca değiştirir.");

        group.MapPost("/mute", async (ToggleMutedTopicCommand command, ISender sender, CancellationToken ct) =>
                Results.Ok(new { muted = await sender.Send(command, ct) }))
            .WithSummary("Bir konuyu sessize alır veya sessizden çıkarır.");

        group.MapPost("/favorite-source", async (
                ToggleFavoriteSourceCommand command,
                ISender sender,
                CancellationToken ct) =>
                Results.Ok(new { favorite = await sender.Send(command, ct) }))
            .WithSummary("Kaynağı favorilere ekler veya çıkarır.");

        group.MapPut("/notifications", async (
                UpdateNotificationSettingsCommand command,
                ISender sender,
                CancellationToken ct) =>
            {
                await sender.Send(command, ct);
                return Results.NoContent();
            })
            .WithSummary("Bildirim tercihlerini günceller.");
    }

    private static void MapLearning(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/learning")
            .WithTags("Learning")
            .RequireAuthorization();

        group.MapGet("/today", async (ISender sender, CancellationToken ct) =>
            {
                var suggestion = await sender.Send(new GetTodayLearningQuery(), ct);
                return suggestion is null ? Results.NoContent() : Results.Ok(suggestion);
            })
            .WithSummary("Bugünün öğrenme önerisi.")
            .Produces<LearningSuggestionDto>()
            .RequireRateLimiting("ai");

        group.MapGet("/history", async ([FromQuery] int? take, ISender sender, CancellationToken ct) =>
                Results.Ok(await sender.Send(new GetLearningHistoryQuery(take ?? 30), ct)))
            .WithSummary("Geçmiş öğrenme önerileri.")
            .Produces<IReadOnlyList<LearningSuggestionDto>>();

        group.MapPut("/{id:guid}/status", async (
                Guid id,
                LearningStatusRequest request,
                ISender sender,
                CancellationToken ct) =>
            {
                await sender.Send(new UpdateLearningStatusCommand(id, request.Status), ct);
                return Results.NoContent();
            })
            .WithSummary("Öğrenme önerisinin durumunu günceller.");
    }

    private static void MapTrends(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/trends", async (
                [FromQuery] DigestPeriod? period,
                [FromQuery] int? take,
                ISender sender,
                CancellationToken ct) =>
                Results.Ok(await sender.Send(
                    new GetTrendsQuery(period ?? DigestPeriod.Monthly, take ?? 20), ct)))
            .WithTags("Trends")
            .WithSummary("En çok konuşulan teknolojiler.")
            .Produces<IReadOnlyList<TrendDto>>()
            .AllowAnonymous();
    }

    public sealed record LearningStatusRequest(LearningStatus Status);
}
