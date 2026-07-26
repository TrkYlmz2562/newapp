using FluentValidation;
using FocusAI.Application.Common.Exceptions;
using FocusAI.Application.Common.Interfaces;
using FocusAI.Application.Common.Mappings;
using FocusAI.Application.Common.Models;
using FocusAI.Application.Dtos;
using FocusAI.Domain.Entities.Users;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FocusAI.Application.Features.Bookmarks;

public sealed record ToggleBookmarkCommand(Guid StoryId, string? Note, IReadOnlyList<string>? Tags)
    : IRequest<bool>;

public sealed class ToggleBookmarkCommandValidator : AbstractValidator<ToggleBookmarkCommand>
{
    public ToggleBookmarkCommandValidator()
    {
        RuleFor(x => x.StoryId).NotEmpty();
        RuleFor(x => x.Note).MaximumLength(2000);
    }
}

/// <summary>Returns true when the story ended up saved, false when it was removed.</summary>
public sealed class ToggleBookmarkCommandHandler(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    IDateTimeProvider clock) : IRequestHandler<ToggleBookmarkCommand, bool>
{
    public async Task<bool> Handle(ToggleBookmarkCommand request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            throw new UnauthorizedException();
        }

        var existing = await db.Bookmarks
            .FirstOrDefaultAsync(b => b.UserId == userId && b.StoryId == request.StoryId, cancellationToken);

        if (existing is not null)
        {
            db.Bookmarks.Remove(existing);
            await db.SaveChangesAsync(cancellationToken);
            return false;
        }

        if (!await db.Stories.AnyAsync(s => s.Id == request.StoryId, cancellationToken))
        {
            throw NotFoundException.For("Haber", request.StoryId);
        }

        db.Bookmarks.Add(new Bookmark
        {
            UserId = userId,
            StoryId = request.StoryId,
            Note = request.Note,
            Tags = request.Tags?.ToList() ?? [],
            CreatedAt = clock.UtcNow
        });

        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}

public sealed record GetBookmarksQuery(int Page = 1, int PageSize = 20, string? Tag = null)
    : IRequest<PagedResult<BookmarkDto>>;

public sealed class GetBookmarksQueryHandler(
    IApplicationDbContext db,
    ICurrentUser currentUser) : IRequestHandler<GetBookmarksQuery, PagedResult<BookmarkDto>>
{
    public async Task<PagedResult<BookmarkDto>> Handle(
        GetBookmarksQuery request,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            throw new UnauthorizedException();
        }

        var query = db.Bookmarks
            .AsNoTracking()
            .Where(b => b.UserId == userId);

        if (!string.IsNullOrWhiteSpace(request.Tag))
        {
            var tag = request.Tag.Trim();
            query = query.Where(b => b.Tags.Contains(tag));
        }

        var card = StoryProjections.ToCard();

        // Projected without the bookmark flag — `with` expressions are not legal
        // inside an expression tree — then stamped in memory below.
        var page = await query
            .OrderByDescending(b => b.CreatedAt)
            .Select(b => new
            {
                b.Id,
                b.Note,
                b.Tags,
                b.CreatedAt,
                Story = db.Stories.Where(s => s.Id == b.StoryId).Select(card).First()
            })
            .ToPagedResultAsync(request.Page, request.PageSize, cancellationToken);

        return new PagedResult<BookmarkDto>
        {
            Items = page.Items
                .Select(b => new BookmarkDto(
                    b.Id,
                    b.Note,
                    b.Tags,
                    b.CreatedAt,
                    b.Story with { IsBookmarked = true }))
                .ToList(),
            Page = page.Page,
            PageSize = page.PageSize,
            TotalCount = page.TotalCount
        };
    }
}
