using FocusAI.Application.Dtos;
using FocusAI.Application.Features.Digests;
using FocusAI.Application.Features.Ingestion;
using FocusAI.Application.Features.Trends;
using FocusAI.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace FocusAI.Api.Endpoints;

/// <summary>
/// Operational endpoints. The same work runs on a schedule; these exist so the
/// pipeline can be driven by hand during development and incident response.
/// </summary>
public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin")
            .WithTags("Admin")
            .RequireAuthorization("admin");

        group.MapPost("/ingest", async (
                [FromQuery] Guid? sourceId,
                [FromQuery] bool? force,
                ISender sender,
                CancellationToken ct) =>
                Results.Ok(await sender.Send(new IngestSourcesCommand(sourceId, force ?? false), ct)))
            .WithSummary("Kaynakları hemen tarar.")
            .Produces<IngestionReportDto>();

        group.MapPost("/cluster", async ([FromQuery] int? batchSize, ISender sender, CancellationToken ct) =>
                Results.Ok(await sender.Send(new ClusterArticlesCommand(batchSize ?? 200), ct)))
            .WithSummary("Bekleyen makaleleri kümeler ve embedding üretir.")
            .Produces<IngestionReportDto>();

        group.MapPost("/enrich", async (
                [FromQuery] int? batchSize,
                [FromQuery] Guid? storyId,
                ISender sender,
                CancellationToken ct) =>
                Results.Ok(await sender.Send(new EnrichStoriesCommand(batchSize ?? 25, storyId), ct)))
            .WithSummary("Taslak haberler için AI özeti ve analizi üretir.")
            .Produces<IngestionReportDto>();

        group.MapPost("/backfill-images", async (
                [FromQuery] int? batchSize,
                ISender sender,
                CancellationToken ct) =>
                Results.Ok(await sender.Send(new BackfillImagesCommand(batchSize ?? 50), ct)))
            .WithSummary("Görseli olmayan eski makaleler için sayfayı bir kez tarayıp og:image çeker.")
            .WithDescription(
                "Tek seferlik onarım. Her makale yalnızca BİR kez denenir (sonuç ne olursa olsun " +
                "işaretlenir), o yüzden articlesRemaining her çağrıda azalır ve sıfıra iner. " +
                "noImageOnPage = sayfada görsel yok (tekrar denemek fayda etmez), " +
                "fetchFailed = sayfaya ulaşılamadı (sonra tekrar denenebilir).")
            .Produces<ImageBackfillReportDto>();

        group.MapPost("/backfill-visual-subjects", async (
                [FromQuery] int? batchSize,
                ISender sender,
                CancellationToken ct) =>
                Results.Ok(await sender.Send(new BackfillVisualSubjectsCommand(batchSize ?? 500), ct)))
            .WithSummary("Eski haberlerin kart öznesini başlıktan türetir (AI çağrısı yok, ücretsiz).")
            .WithDescription(
                "Yayınlanmış haberler yeniden zenginleştirilmediği için özne alanları boş kalır. " +
                "Bu komut onları başlıktan çıkarır; hiçbir dış istek yapmaz ve token harcamaz.")
            .Produces<VisualSubjectBackfillDto>();

        group.MapPost("/pipeline", async (ISender sender, CancellationToken ct) =>
            {
                // Full pipeline in order — the shape PRD section 13 describes.
                var ingest = await sender.Send(new IngestSourcesCommand(), ct);
                var cluster = await sender.Send(new ClusterArticlesCommand(), ct);
                var enrich = await sender.Send(new EnrichStoriesCommand(), ct);

                return Results.Ok(new
                {
                    ingest,
                    cluster,
                    enrich
                });
            })
            .WithSummary("Tüm ingestion hattını sırayla çalıştırır.");

        group.MapPost("/digest", async (
                [FromQuery] Guid? userId,
                [FromQuery] DigestPeriod? period,
                ISender sender,
                CancellationToken ct) =>
                Results.Ok(new
                {
                    digestId = await sender.Send(
                        new GenerateDigestCommand(userId, null, period ?? DigestPeriod.Daily, Force: true), ct)
                }))
            .WithSummary("Özet üretimini tetikler.");

        group.MapPost("/trends", async ([FromQuery] DigestPeriod? period, ISender sender, CancellationToken ct) =>
                Results.Ok(new
                {
                    buckets = await sender.Send(new RebuildTrendsCommand(period ?? DigestPeriod.Monthly), ct)
                }))
            .WithSummary("Trend anlık görüntülerini yeniden hesaplar.");

        return app;
    }
}
