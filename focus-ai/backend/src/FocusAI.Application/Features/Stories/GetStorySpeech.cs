using FocusAI.Application.Common.Exceptions;
using FocusAI.Application.Common.Interfaces;
using FocusAI.Application.Common.Mappings;
using FocusAI.Application.Dtos;
using FocusAI.Domain.Speech;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FocusAI.Application.Features.Stories;

/// <summary>
/// The read-aloud script for one story.
/// </summary>
/// <remarks>
/// Its own endpoint rather than a field on the detail response. Most readers
/// never press play, and the script is a second copy of the story's text — there
/// is no reason to send it to everyone who opens a page.
/// </remarks>
public sealed record GetStorySpeechQuery(string Slug) : IRequest<StorySpeechDto>;

public sealed class GetStorySpeechQueryHandler(IApplicationDbContext db)
    : IRequestHandler<GetStorySpeechQuery, StorySpeechDto>
{
    public async Task<StorySpeechDto> Handle(GetStorySpeechQuery request, CancellationToken cancellationToken)
    {
        var slug = request.Slug.Trim().ToLowerInvariant();

        var story = await db.Stories
            .AsNoTracking()
            .Where(StoryFilters.VisibleToReaders)
            .Where(s => s.Slug == slug)
            .Select(s => new
            {
                s.Id,
                s.Slug,
                s.Title,
                s.Dek,
                Summary = s.Summary == null ? null : s.Summary.Summary,
                WhyItMatters = s.Summary == null ? null : s.Summary.WhyItMatters,
                WhoIsAffected = s.Summary == null ? null : s.Summary.WhoIsAffected,
                WhatShouldIDo = s.Summary == null ? null : s.Summary.WhatShouldIDo,
                KeyPoints = s.Summary == null ? new List<string>() : s.Summary.KeyPoints
            })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw NotFoundException.For("Haber", request.Slug);

        var chunks = SpeechScript.Build(new SpeechSource
        {
            Title = story.Title,
            Dek = story.Dek,
            Summary = story.Summary,
            WhyItMatters = story.WhyItMatters,
            WhoIsAffected = story.WhoIsAffected,
            WhatShouldIDo = story.WhatShouldIDo,
            KeyPoints = story.KeyPoints
        });

        return new StorySpeechDto(
            story.Id,
            story.Slug,
            story.Title,
            SpeechDuration.SecondsFor(chunks),
            chunks.Select(chunk => new SpeechChunkDto(chunk.Text, chunk.Kind)).ToList());
    }
}
