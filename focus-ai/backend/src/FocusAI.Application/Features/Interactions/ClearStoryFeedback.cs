using FocusAI.Application.Common.Exceptions;
using FocusAI.Application.Common.Interfaces;
using FocusAI.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FocusAI.Application.Features.Interactions;

/// <summary>
/// Retracts the reader's "faydalı" / "az göster" verdict on one story.
///
/// Interactions are otherwise append-only, and the ranker reads the newest row
/// per story — which leaves no way to get back to "no opinion" by writing another
/// row. Undo therefore has to delete. Only this reader's verdict rows on this one
/// story are touched; reading history, saves and shares are untouched.
/// </summary>
public sealed record ClearStoryFeedbackCommand(Guid StoryId) : IRequest<Unit>;

public sealed class ClearStoryFeedbackCommandHandler(
    IApplicationDbContext db,
    ICurrentUser currentUser) : IRequestHandler<ClearStoryFeedbackCommand, Unit>
{
    public async Task<Unit> Handle(ClearStoryFeedbackCommand request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            throw new UnauthorizedException();
        }

        var verdicts = await db.Interactions
            .Where(i => i.UserId == userId &&
                        i.StoryId == request.StoryId &&
                        (i.Type == InteractionType.Helpful || i.Type == InteractionType.NotHelpful))
            .ToListAsync(cancellationToken);

        // Nothing to retract is a success, not a 404: the caller wanted no verdict
        // on this story and that is already true.
        if (verdicts.Count == 0)
        {
            return Unit.Value;
        }

        db.Interactions.RemoveRange(verdicts);
        await db.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
