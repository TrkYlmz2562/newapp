using FocusAI.Application.Common.Models;
using FocusAI.Application.Dtos;
using FocusAI.Application.Features.Ask;
using FocusAI.Application.Features.Catalog;
using FocusAI.Application.Features.Interactions;
using FocusAI.Application.Features.Search;
using FocusAI.Application.Features.Stories;
using FocusAI.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace FocusAI.Api.Endpoints;

public static class StoryEndpoints
{
    public static IEndpointRouteBuilder MapStoryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/stories").WithTags("Stories");

        group.MapGet("/", async (
                [FromQuery] ContentCategory? category,
                [FromQuery] string? topic,
                [FromQuery] int? page,
                [FromQuery] int? pageSize,
                [FromQuery] int? withinHours,
                [FromQuery] bool? personalized,
                ISender sender,
                CancellationToken ct) =>
                Results.Ok(await sender.Send(new GetStoryFeedQuery
                {
                    Category = category,
                    TopicSlug = topic,
                    Page = page ?? 1,
                    PageSize = pageSize ?? 20,
                    WithinHours = withinHours,
                    Personalized = personalized ?? true
                }, ct)))
            .WithSummary("Ana akış. Oturum açıksa kişiselleştirilir.")
            .Produces<PagedResult<StoryCardDto>>()
            .AllowAnonymous();

        group.MapGet("/top", async ([FromQuery] int? withinHours, ISender sender, CancellationToken ct) =>
            {
                var story = await sender.Send(new GetTopStoryQuery(withinHours ?? 24), ct);
                return story is null ? Results.NoContent() : Results.Ok(story);
            })
            .WithSummary("Günün en önemli gelişmesi.")
            .Produces<StoryCardDto>()
            .AllowAnonymous();

        // Ahead of "/{slug}" for the reader's benefit, not the router's — a literal
        // segment outscores a parameter one wherever it is declared.
        group.MapGet("/unread-count", async (
                [FromQuery] ContentCategory? category,
                ISender sender,
                CancellationToken ct) =>
                Results.Ok(await sender.Send(new GetUnreadCountQuery(category), ct)))
            .WithSummary("Feed'de kaç haber açılmamış.")
            .Produces<UnreadCountDto>()
            .RequireAuthorization();

        group.MapGet("/{slug}", async (string slug, ISender sender, CancellationToken ct) =>
                Results.Ok(await sender.Send(new GetStoryDetailQuery(slug), ct)))
            .WithSummary("Haber detayı: özet, AI yorumu, kaynaklar, ilgili haberler.")
            .Produces<StoryDetailDto>()
            .AllowAnonymous();

        // Its own route rather than a field on the detail response: the script is
        // a second copy of the story's text, and most readers never press play.
        group.MapGet("/{slug}/speech", async (string slug, ISender sender, CancellationToken ct) =>
                Results.Ok(await sender.Send(new GetStorySpeechQuery(slug), ct)))
            .WithSummary("Haberin sesli okuma metni, cümlelere bölünmüş.")
            .Produces<StorySpeechDto>()
            .AllowAnonymous();

        group.MapPost("/{storyId:guid}/interactions", async (
                Guid storyId,
                InteractionRequest request,
                ISender sender,
                CancellationToken ct) =>
            {
                await sender.Send(
                    new RecordInteractionCommand(storyId, request.Type, request.DwellSeconds, request.Surface),
                    ct);

                return Results.NoContent();
            })
            .WithSummary("Okuma/kaydetme gibi etkileşimleri kaydeder.")
            .RequireAuthorization();

        group.MapDelete("/{storyId:guid}/interactions/feedback", async (
                Guid storyId,
                ISender sender,
                CancellationToken ct) =>
            {
                await sender.Send(new ClearStoryFeedbackCommand(storyId), ct);
                return Results.NoContent();
            })
            .WithSummary("\"Faydalı\" / \"az göster\" tercihini geri alır.")
            .WithDescription(
                "Etkileşimler normalde silinmez; sıralama her haber için en son " +
                "tercihi okuduğundan \"tercihim yok\" durumuna dönmenin tek yolu budur. " +
                "Yalnızca bu haberdeki bu iki tercih silinir, okuma geçmişi korunur.")
            .RequireAuthorization();

        app.MapGet("/api/search", async (
                [FromQuery] string q,
                [FromQuery] int? page,
                [FromQuery] int? pageSize,
                ISender sender,
                CancellationToken ct) =>
                Results.Ok(await sender.Send(new SearchStoriesQuery(q, page ?? 1, pageSize ?? 20), ct)))
            .WithTags("Search")
            .WithSummary("Doğal dil destekli arama.")
            .Produces<PagedResult<StoryCardDto>>()
            .AllowAnonymous();

        app.MapPost("/api/ask", async (AskQuestionCommand command, ISender sender, CancellationToken ct) =>
                Results.Ok(await sender.Send(command, ct)))
            .WithTags("Search")
            .WithSummary("Haber arşivi üzerinde soru-cevap.")
            .Produces<AskResultDto>()
            .RequireAuthorization()
            .RequireRateLimiting("ai");

        app.MapGet("/api/topics", async (
                [FromQuery] bool? all,
                [FromQuery] TopicKind? kind,
                ISender sender,
                CancellationToken ct) =>
                Results.Ok(await sender.Send(new GetTopicsQuery(!(all ?? false), kind), ct)))
            .WithTags("Catalog")
            .WithSummary("İlgi alanı seçimi için konu listesi.")
            .Produces<IReadOnlyList<TopicDto>>()
            .AllowAnonymous();

        app.MapGet("/api/sources", async (
                [FromQuery] SourceCategory? category,
                ISender sender,
                CancellationToken ct) =>
                Results.Ok(await sender.Send(new GetSourcesQuery(category), ct)))
            .WithTags("Catalog")
            .WithSummary("Takip edilen kaynaklar.")
            .Produces<IReadOnlyList<SourceDto>>()
            .AllowAnonymous();

        app.MapGet("/api/sources/health", async (ISender sender, CancellationToken ct) =>
                Results.Ok(await sender.Send(new GetSourceHealthQuery(), ct)))
            .WithTags("Catalog")
            .WithSummary("Kaynakların gerçekten çalışıp çalışmadığı.")
            .WithDescription(
                "Bir akış HTTP 200 dönüp aylardır güncellenmemiş içerik servis edebilir; " +
                "tarama başarılı görünür ve kaynak yeşil kalır. Bu uç, sessizliği " +
                "kaynağın kendi yayın ritmine göre değerlendirir — haftada bir yazan " +
                "bir blog 10 günde bayat değildir, saatte bir yazan bir ajans öyledir.")
            .Produces<SourceHealthReportDto>()
            .AllowAnonymous();

        return app;
    }

    public sealed record InteractionRequest(InteractionType Type, int? DwellSeconds, string? Surface);
}
