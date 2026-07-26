using FluentValidation;
using FocusAI.Application.Common.Interfaces;
using FocusAI.Application.Common.Mappings;
using FocusAI.Application.Common.Services;
using FocusAI.Application.Dtos;
using FocusAI.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FocusAI.Application.Features.Ask;

/// <summary>
/// The research-assistant endpoint from PRD section 19: "Bu hafta .NET
/// ekosisteminde ne değişti?" Retrieval over the story corpus, then a grounded
/// answer that cites the stories it used.
/// </summary>
public sealed record AskQuestionCommand(string Question) : IRequest<AskResultDto>;

public sealed class AskQuestionCommandValidator : AbstractValidator<AskQuestionCommand>
{
    public AskQuestionCommandValidator()
    {
        RuleFor(x => x.Question).NotEmpty().MinimumLength(5).MaximumLength(1000);
    }
}

public sealed class AskQuestionCommandHandler(
    IApplicationDbContext db,
    IContentAiService ai,
    IEmbeddingService embeddings,
    IVectorSearch vectorSearch,
    IReaderContextFactory readerContextFactory,
    ICurrentUser currentUser,
    IDateTimeProvider clock) : IRequestHandler<AskQuestionCommand, AskResultDto>
{
    /// <summary>How many stories are retrieved into the answer context.</summary>
    private const int RetrievalDepth = 8;

    public async Task<AskResultDto> Handle(AskQuestionCommand request, CancellationToken cancellationToken)
    {
        var snapshot = await readerContextFactory.BuildAsync(currentUser.UserId, cancellationToken);

        // The parsed filter narrows retrieval by date/topic so "son 3 ayda"
        // actually constrains the corpus rather than just flavouring the prompt.
        var parsed = await ai.ParseSearchQueryAsync(request.Question, clock.UtcNow, cancellationToken);

        var candidates = await RetrieveAsync(request.Question, parsed, cancellationToken);

        if (candidates.Count == 0)
        {
            return new AskResultDto(
                "Bu soruya yanıt verecek yeterli veri bulunamadı. Farklı bir şekilde sormayı deneyebilirsin.",
                0d,
                []);
        }

        var context = candidates
            .Select(c => (c.Id, c.Title, Summary: c.Summary ?? string.Empty))
            .ToList();

        var answer = await ai.AnswerAsync(request.Question, context, snapshot.Language, cancellationToken);

        var citedIds = answer.CitedStoryIds.Count > 0
            ? answer.CitedStoryIds
            : candidates.Take(3).Select(c => c.Id).ToList();

        var citations = await db.Stories
            .AsNoTracking()
            .Where(s => citedIds.Contains(s.Id))
            .Select(StoryProjections.ToCard())
            .ToListAsync(cancellationToken);

        return new AskResultDto(answer.Answer, answer.Confidence, citations);
    }

    private async Task<IReadOnlyList<RetrievedStory>> RetrieveAsync(
        string question,
        ParsedSearchQuery parsed,
        CancellationToken cancellationToken)
    {
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

        if (parsed.TopicSlugs.Count > 0)
        {
            var slugs = parsed.TopicSlugs.Select(s => s.ToLowerInvariant()).ToArray();
            query = query.Where(s => s.Topics.Any(t => slugs.Contains(t.Topic!.Slug)));
        }

        try
        {
            var embedding = await embeddings.EmbedAsync(question, cancellationToken);
            var hits = await vectorSearch.FindSimilarStoriesAsync(
                embedding, RetrievalDepth * 3, null, cancellationToken);

            if (hits.Count > 0)
            {
                var hitIds = hits.Select(h => h.StoryId).ToList();
                var semantic = await query
                    .Where(s => hitIds.Contains(s.Id))
                    .Select(s => new RetrievedStory(
                        s.Id,
                        s.Title,
                        s.Summary != null ? s.Summary.Summary : null))
                    .Take(RetrievalDepth)
                    .ToListAsync(cancellationToken);

                if (semantic.Count > 0)
                {
                    return semantic;
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Embeddings unavailable — fall through to the keyword path below.
        }

        var keywords = Domain.Text.TextNormalizer.Tokenize(question)
            .OrderByDescending(t => t.Length)
            .Take(3)
            .ToArray();

        foreach (var keyword in keywords)
        {
            var needle = keyword;
            query = query.Where(s =>
                s.Title.ToLower().Contains(needle) ||
                (s.Summary != null && s.Summary.Summary.ToLower().Contains(needle)));
        }

        return await query
            .OrderByDescending(s => s.ImportanceScore)
            .ThenByDescending(s => s.PublishedAt)
            .Take(RetrievalDepth)
            .Select(s => new RetrievedStory(s.Id, s.Title, s.Summary != null ? s.Summary.Summary : null))
            .ToListAsync(cancellationToken);
    }

    private sealed record RetrievedStory(Guid Id, string Title, string? Summary);
}
