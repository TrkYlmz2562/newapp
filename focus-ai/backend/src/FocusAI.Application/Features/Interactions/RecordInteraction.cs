using FluentValidation;
using FocusAI.Application.Common.Exceptions;
using FocusAI.Application.Common.Interfaces;
using FocusAI.Domain.Entities.Gamification;
using FocusAI.Domain.Entities.Users;
using FocusAI.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FocusAI.Application.Features.Interactions;

/// <summary>
/// Records a behavioural signal. Feeds personalisation, the reading streak and
/// the KPI set in PRD section 18.
/// </summary>
public sealed record RecordInteractionCommand(
    Guid StoryId,
    InteractionType Type,
    int? DwellSeconds,
    string? Surface) : IRequest<Unit>;

public sealed class RecordInteractionCommandValidator : AbstractValidator<RecordInteractionCommand>
{
    public RecordInteractionCommandValidator()
    {
        RuleFor(x => x.StoryId).NotEmpty();
        RuleFor(x => x.DwellSeconds)
            .InclusiveBetween(0, 3600)
            .When(x => x.DwellSeconds.HasValue)
            .WithMessage("Okuma süresi 0-3600 saniye aralığında olmalı.");
        RuleFor(x => x.Surface).MaximumLength(40);
    }
}

public sealed class RecordInteractionCommandHandler(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    IDateTimeProvider clock) : IRequestHandler<RecordInteractionCommand, Unit>
{
    public async Task<Unit> Handle(RecordInteractionCommand request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            throw new UnauthorizedException();
        }

        if (!await db.Stories.AnyAsync(s => s.Id == request.StoryId, cancellationToken))
        {
            throw NotFoundException.For("Haber", request.StoryId);
        }

        var now = clock.UtcNow;

        db.Interactions.Add(new Interaction
        {
            UserId = userId,
            StoryId = request.StoryId,
            Type = request.Type,
            DwellSeconds = request.DwellSeconds,
            Surface = request.Surface,
            OccurredAt = now
        });

        // Only genuine reading advances the streak — an impression is not activity.
        if (request.Type is InteractionType.Open or InteractionType.ReadComplete)
        {
            var user = await db.Users
                .Include(u => u.Streak)
                .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

            if (user is not null)
            {
                user.Streak ??= new UserStreak { UserId = userId, CreatedAt = now };
                user.Streak.RegisterActivity(clock.TodayIn(user.TimeZone));

                if (request.Type == InteractionType.ReadComplete)
                {
                    user.Streak.TotalStoriesRead++;
                }
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
