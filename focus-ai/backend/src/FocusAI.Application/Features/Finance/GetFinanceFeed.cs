using FocusAI.Application.Common.Interfaces;
using FocusAI.Application.Dtos;
using FocusAI.Domain.Enums;
using FocusAI.Domain.Text;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FocusAI.Application.Features.Finance;

/// <summary>
/// The Finans section. Two views over the same gate: developments that have been
/// formally committed, and ones that are real but still waiting on a named approval.
/// </summary>
/// <remarks>
/// This endpoint answers "what has been decided", never "what will happen". Nothing
/// here is ranked by a certainty score — items are ordered by when the event occurs,
/// because ordering by certainty would read as a quality ranking and invite the
/// reader to treat position as significance.
/// </remarks>
public sealed record GetFinanceFeedQuery(bool Conditional = false, int Take = 40)
    : IRequest<FinanceFeedDto>;

public sealed class GetFinanceFeedQueryHandler(IApplicationDbContext db, IDateTimeProvider clock)
    : IRequestHandler<GetFinanceFeedQuery, FinanceFeedDto>
{
    /// <summary>
    /// A floor, never a contributor: the existing blended trust score gates entry
    /// but is never shown on a finance card, where a 0-100 number beside a financial
    /// claim reads as precision the product does not have.
    /// </summary>
    private const int MinTrustScore = 70;

    public async Task<FinanceFeedDto> Handle(GetFinanceFeedQuery request, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var take = Math.Clamp(request.Take, 1, 100);

        var candidates = await db.Stories
            .AsNoTracking()
            .Where(s => s.Status == StoryStatus.Published &&
                        s.Category == ContentCategory.Finance &&
                        s.Commitment != null &&
                        !s.Commitment.IsReversed &&
                        s.TrustScore >= MinTrustScore)
            .Include(s => s.Commitment)
            .Include(s => s.Topics).ThenInclude(t => t.Topic)
            .OrderBy(s => s.Commitment!.EventDate ?? DateOnly.MaxValue)
            .Take(500)
            .ToListAsync(cancellationToken);

        // The matrix is applied in memory: it is a small, readable rule set that
        // must stay identical to the one the tests pin, and expressing it as SQL
        // would split it across two places.
        var items = candidates
            .Select(story => new
            {
                Story = story,
                Commitment = story.Commitment!
            })
            .Where(x => request.Conditional
                ? CommitmentLexicon.IsConditional(x.Commitment.Tier, x.Commitment.Horizon)
                : CommitmentLexicon.IsPublishable(x.Commitment.Tier, x.Commitment.Horizon, x.Commitment.Instrument))
            .Take(take)
            .Select(x => new FinanceItemDto(
                x.Story.Id,
                x.Story.Slug,
                x.Story.Title,
                x.Commitment.Tier,
                x.Commitment.Horizon,
                x.Commitment.Instrument,
                x.Commitment.Event,
                x.Commitment.DateText,
                x.Commitment.EventDate,
                x.Commitment.DatePrecision,
                x.Commitment.Quote,
                x.Commitment.Condition,
                x.Commitment.Reference,
                x.Story.SourceCount,
                x.Story.PublishedAt,
                x.Story.Topics
                    .OrderByDescending(t => t.Weight)
                    .Select(t => t.Topic!.Name)
                    .Take(3)
                    .ToList()))
            .ToList();

        // The reader asked for two things: what is coming up, and what is fixed but
        // further out. Realized items are kept apart so the forward list stays a
        // forward list.
        return new FinanceFeedDto(
            Realized: items.Where(i => i.Horizon == EventHorizon.Completed).ToList(),
            Soon: items.Where(i => i.Horizon is EventHorizon.Imminent or EventHorizon.Near).ToList(),
            Later: items.Where(i => i.Horizon is EventHorizon.Mid or EventHorizon.Long).ToList(),
            GeneratedAt: now);
    }
}
