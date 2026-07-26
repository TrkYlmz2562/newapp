using FluentValidation;
using FocusAI.Application.Common.Interfaces;
using FocusAI.Application.Common.Mappings;
using FocusAI.Application.Common.Models;
using FocusAI.Application.Dtos;
using FocusAI.Domain.Entities.Search;
using FocusAI.Domain.Enums;
using FocusAI.Domain.Text;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FocusAI.Application.Features.Search;

/// <summary>
/// Natural-language search from PRD section 9, e.g. "Son bir ayda çıkan tüm AI
/// Agent haberlerini göster." The query is first parsed into a structured filter,
/// then executed against the search index (or the database fallback).
/// </summary>
public sealed record SearchStoriesQuery(string Query, int Page = 1, int PageSize = 20)
    : IRequest<PagedResult<StoryCardDto>>;

public sealed class SearchStoriesQueryValidator : AbstractValidator<SearchStoriesQuery>
{
    public SearchStoriesQueryValidator()
    {
        RuleFor(x => x.Query).NotEmpty().MaximumLength(500);
    }
}

public sealed class SearchStoriesQueryHandler(
    IApplicationDbContext db,
    IContentAiService ai,
    ISearchIndex searchIndex,
    ICurrentUser currentUser,
    IDateTimeProvider clock) : IRequestHandler<SearchStoriesQuery, PagedResult<StoryCardDto>>
{
    public async Task<PagedResult<StoryCardDto>> Handle(
        SearchStoriesQuery request,
        CancellationToken cancellationToken)
    {
        var started = clock.UtcNow;
        var parsed = await ai.ParseSearchQueryAsync(request.Query, started, cancellationToken);

        var query = db.Stories
            .AsNoTracking()
            .Where(s => s.Status == StoryStatus.Published);

        if (parsed.From is { } from)
        {
            query = query.Where(s => s.PublishedAt >= from);
        }

        if (parsed.To is { } to)
        {
            query = query.Where(s => s.PublishedAt <= to);
        }

        if (parsed.Category is { } category)
        {
            query = query.Where(s => s.Category == category);
        }

        if (parsed.MinTrustScore is { } minTrust)
        {
            query = query.Where(s => s.TrustScore >= minTrust);
        }

        if (parsed.OfficialSourcesOnly)
        {
            query = query.Where(s => s.OfficialSourceCount > 0);
        }

        if (parsed.TopicSlugs.Count > 0)
        {
            var slugs = parsed.TopicSlugs.Select(s => s.ToLowerInvariant()).ToArray();
            query = query.Where(s => s.Topics.Any(t => slugs.Contains(t.Topic!.Slug)));
        }

        var text = string.IsNullOrWhiteSpace(parsed.Text) ? request.Query : parsed.Text;

        PagedResult<StoryCardDto> result;

        // Prefer the dedicated index when it is up; otherwise fall back to
        // token matching in the database so search degrades rather than dies.
        if (searchIndex.IsEnabled && !string.IsNullOrWhiteSpace(text))
        {
            var hits = await searchIndex.SearchAsync(text, 200, cancellationToken);
            var hitIds = hits.Select(h => h.StoryId).ToList();

            result = hitIds.Count == 0
                ? PagedResult<StoryCardDto>.Empty(request.Page, request.PageSize)
                : await query
                    .Where(s => hitIds.Contains(s.Id))
                    .OrderByDescending(s => s.ImportanceScore)
                    .ThenByDescending(s => s.PublishedAt)
                    .Select(StoryProjections.ToCard())
                    .ToPagedResultAsync(request.Page, request.PageSize, cancellationToken);
        }
        else
        {
            result = await SearchByTokensAsync(query, text, request, cancellationToken);
        }

        db.SearchLogs.Add(new SearchLog
        {
            UserId = currentUser.UserId,
            RawQuery = request.Query,
            ResultCount = result.TotalCount,
            UsedLlmParsing = parsed.ParsedByLlm,
            LatencyMs = (int)(clock.UtcNow - started).TotalMilliseconds,
            OccurredAt = started
        });

        await db.SaveChangesAsync(cancellationToken);

        return result;
    }

    /// <summary>
    /// Keyword search without an external index.
    /// </summary>
    /// <remarks>
    /// Matching the query as one literal string finds nothing the moment a user
    /// types a sentence rather than a keyword, which is exactly what PRD section
    /// 9 invites them to do. So: OR across tokens in SQL to get a candidate set,
    /// then rank in memory by how many tokens actually matched, weighting title
    /// hits above body hits. The candidate cap keeps the in-memory pass bounded.
    /// </remarks>
    private async Task<PagedResult<StoryCardDto>> SearchByTokensAsync(
        IQueryable<Domain.Entities.Content.Story> query,
        string text,
        SearchStoriesQuery request,
        CancellationToken cancellationToken)
    {
        const int candidateCap = 300;

        var tokens = TextNormalizer.Tokenize(text)
            .Distinct()
            .Take(6)
            .ToArray();

        if (tokens.Length == 0)
        {
            return await query
                .OrderByDescending(s => s.ImportanceScore)
                .ThenByDescending(s => s.PublishedAt)
                .Select(StoryProjections.ToCard())
                .ToPagedResultAsync(request.Page, request.PageSize, cancellationToken);
        }

        // Provider-agnostic on purpose: the Application layer must not depend on
        // Npgsql's ILike. LOWER(...) LIKE translates on every provider.
        var predicate = PredicateFor(tokens);

        var candidates = await query
            .Where(predicate)
            .OrderByDescending(s => s.ImportanceScore)
            .Take(candidateCap)
            .Select(StoryProjections.ToCard())
            .ToListAsync(cancellationToken);

        var ranked = candidates
            .Select(card => new
            {
                Card = card,
                Score = Relevance(card, tokens)
            })
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Card.ImportanceScore)
            .ThenByDescending(x => x.Card.PublishedAt)
            .Select(x => x.Card)
            .ToList();

        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);

        return new PagedResult<StoryCardDto>
        {
            Items = ranked.Skip((page - 1) * pageSize).Take(pageSize).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalCount = ranked.Count
        };
    }

    private static System.Linq.Expressions.Expression<Func<Domain.Entities.Content.Story, bool>>
        PredicateFor(IReadOnlyList<string> tokens)
    {
        var parameter = System.Linq.Expressions.Expression.Parameter(
            typeof(Domain.Entities.Content.Story), "s");

        System.Linq.Expressions.Expression? body = null;

        foreach (var token in tokens)
        {
            var needle = token;
            System.Linq.Expressions.Expression<Func<Domain.Entities.Content.Story, bool>> clause =
                s => s.Title.ToLower().Contains(needle) ||
                     (s.Dek != null && s.Dek.ToLower().Contains(needle)) ||
                     (s.Summary != null && s.Summary.Summary.ToLower().Contains(needle));

            var replaced = new ParameterReplacer(clause.Parameters[0], parameter).Visit(clause.Body);
            body = body is null
                ? replaced
                : System.Linq.Expressions.Expression.OrElse(body, replaced!);
        }

        return System.Linq.Expressions.Expression.Lambda<Func<Domain.Entities.Content.Story, bool>>(
            body!, parameter);
    }

    private static int Relevance(StoryCardDto card, IReadOnlyList<string> tokens)
    {
        var title = TextNormalizer.Normalize(card.Title);
        var dek = TextNormalizer.Normalize(card.Dek);
        var summary = TextNormalizer.Normalize(card.Summary);
        var topics = string.Join(' ', card.Topics.Select(t => t.Slug));

        var score = 0;

        foreach (var token in tokens)
        {
            if (topics.Contains(token, StringComparison.Ordinal))
            {
                score += 5;
            }

            if (title.Contains(token, StringComparison.Ordinal))
            {
                score += 4;
            }
            else if (dek.Contains(token, StringComparison.Ordinal))
            {
                score += 2;
            }
            else if (summary.Contains(token, StringComparison.Ordinal))
            {
                score += 1;
            }
        }

        return score;
    }

    /// <summary>Rebinds a lambda parameter so per-token clauses can be OR-ed together.</summary>
    private sealed class ParameterReplacer(
        System.Linq.Expressions.ParameterExpression from,
        System.Linq.Expressions.ParameterExpression to)
        : System.Linq.Expressions.ExpressionVisitor
    {
        protected override System.Linq.Expressions.Expression VisitParameter(
            System.Linq.Expressions.ParameterExpression node) =>
            node == from ? to : base.VisitParameter(node);
    }
}
