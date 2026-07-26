using FocusAI.Application.Common.Interfaces;
using FocusAI.Domain.Common;
using FocusAI.Domain.Text;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FocusAI.Application.Features.Ingestion;

/// <summary>
/// Fills the card subject for stories summarised before the field existed.
/// </summary>
/// <remarks>
/// Enrichment only ever visits Draft/Enriching stories, so without this every
/// already-published story would render its category label in display type —
/// identical on every card, which is precisely the failure the subject was added
/// to remove. Costs nothing: no model call and no page fetch, just the deterministic
/// extractor over titles already in the database.
/// </remarks>
public sealed record BackfillVisualSubjectsCommand(int BatchSize = 500) : IRequest<VisualSubjectBackfillDto>;

public sealed record VisualSubjectBackfillDto(int Scanned, int Filled, int NoSubjectInTitle, int Remaining);

public sealed class BackfillVisualSubjectsCommandHandler(
    IApplicationDbContext db,
    ILogger<BackfillVisualSubjectsCommandHandler> logger)
    : IRequestHandler<BackfillVisualSubjectsCommand, VisualSubjectBackfillDto>
{
    private const int MaxBatch = 2000;

    public async Task<VisualSubjectBackfillDto> Handle(
        BackfillVisualSubjectsCommand request,
        CancellationToken cancellationToken)
    {
        var take = Math.Clamp(request.BatchSize, 1, MaxBatch);

        var summaries = await db.StorySummaries
            .Include(s => s.Story)
            .Where(s => s.VisualEntity == null)
            .OrderByDescending(s => s.GeneratedAt)
            .Take(take)
            .ToListAsync(cancellationToken);

        var filled = 0;

        foreach (var summary in summaries)
        {
            var subject = VisualSubject.FromTitle(summary.Story?.Title, FieldLimits.VisualEntity);
            if (string.IsNullOrWhiteSpace(subject))
            {
                // Left null on purpose. The card falls back to the story's own
                // topics, which is still real story data — writing a placeholder
                // here would only make the row look done.
                continue;
            }

            summary.VisualEntity = subject;
            filled++;
        }

        await db.SaveChangesAsync(cancellationToken);

        var remaining = await db.StorySummaries
            .CountAsync(s => s.VisualEntity == null, cancellationToken);

        logger.LogInformation(
            "FocusAI visual-subject backfill: {Filled}/{Scanned} filled, {Remaining} summaries left",
            filled,
            summaries.Count,
            remaining);

        return new VisualSubjectBackfillDto(summaries.Count, filled, summaries.Count - filled, remaining);
    }
}
