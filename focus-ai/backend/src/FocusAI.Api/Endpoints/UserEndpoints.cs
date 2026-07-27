using FocusAI.Application.Common.Models;
using FocusAI.Application.Dtos;
using FocusAI.Application.Features.Bookmarks;
using FocusAI.Application.Features.Digests;
using FocusAI.Application.Features.Learning;
using FocusAI.Application.Features.Profile;
using FocusAI.Application.Features.Trends;
using FocusAI.Domain.Enums;
using FocusAI.Domain.Learning;
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

        MapBriefs(group);
    }

    /// <summary>
    /// Lesson briefs: what a saved story becomes when the reader wants to study it
    /// rather than just have read it.
    /// </summary>
    private static void MapBriefs(RouteGroupBuilder group)
    {
        // Static product text, and the profile page needs it before it has anything
        // else — no reason to make it an authenticated read.
        group.MapGet("/persona", () => Results.Ok(new PersonaDto(
                FocusMentorPersona.Name,
                FocusMentorPersona.Version,
                FocusMentorPersona.Text)))
            .WithSummary("Claude'da bir kez kurulacak Focus Mentor talimatı.")
            .Produces<PersonaDto>()
            .AllowAnonymous();

        group.MapGet("/briefs", async ([FromQuery] int? take, ISender sender, CancellationToken ct) =>
                Results.Ok(await sender.Send(new GetBriefsQuery(take ?? 50), ct)))
            .WithSummary("Öğrenme kuyruğu ve üretilmiş brifler.")
            .Produces<IReadOnlyList<LearningBriefDto>>();

        group.MapGet("/briefs/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
                Results.Ok(await sender.Send(new GetBriefQuery(id), ct)))
            .WithSummary("Bir brif, kopyalanacak promt dâhil.")
            .Produces<LearningBriefDto>();

        group.MapPost("/briefs", async (
                QueueBriefRequest request,
                ISender sender,
                CancellationToken ct) =>
            {
                if (request.SuggestionId is { } suggestionId)
                {
                    return Results.Ok(await sender.Send(new QueueDailyBriefCommand(suggestionId), ct));
                }

                if (request.StoryId is { } storyId)
                {
                    return Results.Ok(await sender.Send(new QueueStoryBriefCommand(storyId), ct));
                }

                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["storyId"] = ["storyId ya da suggestionId verilmeli."]
                });
            })
            .WithSummary("Bir haberi ya da günün önerisini öğrenme kuyruğuna alır.")
            .Produces<LearningBriefDto>();

        // The only route here that spends a model call, so it is the only one that
        // takes the AI rate limit.
        group.MapPost("/briefs/{id:guid}/generate", async (
                Guid id,
                ISender sender,
                CancellationToken ct) =>
                Results.Ok(await sender.Send(new GenerateBriefCommand(id), ct)))
            .WithSummary("Brifin promtunu üretir ve kaydeder.")
            .Produces<LearningBriefDto>()
            .RequireRateLimiting("ai");

        group.MapPut("/briefs/{id:guid}/status", async (
                Guid id,
                BriefStatusRequest request,
                ISender sender,
                CancellationToken ct) =>
            {
                await sender.Send(new UpdateBriefStatusCommand(id, request.Status), ct);
                return Results.NoContent();
            })
            .WithSummary("Brifin durumunu günceller.");

        group.MapDelete("/briefs/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(new DeleteBriefCommand(id), ct);
                return Results.NoContent();
            })
            .WithSummary("Brifi siler.");
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

    /// <summary>
    /// Exactly one of the two is set: a story from the feed, or the day's
    /// suggestion. Validated in the handler rather than by shape, so the frontend
    /// has one endpoint to call for both entry points.
    /// </summary>
    public sealed record QueueBriefRequest(Guid? StoryId, Guid? SuggestionId);

    public sealed record BriefStatusRequest(BriefStatus Status);

    public sealed record PersonaDto(string Name, int Version, string Text);
}
