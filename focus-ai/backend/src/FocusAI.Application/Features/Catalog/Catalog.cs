using FocusAI.Application.Common.Interfaces;
using FocusAI.Application.Dtos;
using FocusAI.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FocusAI.Application.Features.Catalog;

/// <summary>Topic vocabulary for the onboarding picker and the Explore screen.</summary>
public sealed record GetTopicsQuery(bool SuggestableOnly = true, TopicKind? Kind = null)
    : IRequest<IReadOnlyList<TopicDto>>;

public sealed class GetTopicsQueryHandler(IApplicationDbContext db)
    : IRequestHandler<GetTopicsQuery, IReadOnlyList<TopicDto>>
{
    public async Task<IReadOnlyList<TopicDto>> Handle(
        GetTopicsQuery request,
        CancellationToken cancellationToken)
    {
        var query = db.Topics.AsNoTracking();

        if (request.SuggestableOnly)
        {
            query = query.Where(t => t.IsSuggestable);
        }

        if (request.Kind is { } kind)
        {
            query = query.Where(t => t.Kind == kind);
        }

        return await query
            .OrderBy(t => t.Name)
            .Select(t => new TopicDto(t.Id, t.Name, t.Slug, t.Kind))
            .ToListAsync(cancellationToken);
    }
}

public sealed record GetSourcesQuery(SourceCategory? Category = null, bool IncludeDisabled = false)
    : IRequest<IReadOnlyList<SourceDto>>;

public sealed class GetSourcesQueryHandler(IApplicationDbContext db)
    : IRequestHandler<GetSourcesQuery, IReadOnlyList<SourceDto>>
{
    public async Task<IReadOnlyList<SourceDto>> Handle(
        GetSourcesQuery request,
        CancellationToken cancellationToken)
    {
        var query = db.Sources.AsNoTracking();

        if (!request.IncludeDisabled)
        {
            query = query.Where(s => s.IsEnabled);
        }

        if (request.Category is { } category)
        {
            query = query.Where(s => s.Category == category);
        }

        return await query
            .OrderByDescending(s => s.IsOfficial)
            .ThenBy(s => s.Name)
            .Select(s => new SourceDto(
                s.Id,
                s.Name,
                s.Slug,
                s.WebsiteUrl,
                s.Kind,
                s.Category,
                s.IsOfficial,
                s.IsEnabled,
                s.IconUrl,
                s.LastSucceededAt,
                s.ConsecutiveFailures))
            .ToListAsync(cancellationToken);
    }
}
