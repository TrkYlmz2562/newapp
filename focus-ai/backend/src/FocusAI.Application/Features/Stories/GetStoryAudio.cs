using System.Security.Cryptography;
using System.Text;
using FocusAI.Application.Common.Exceptions;
using FocusAI.Application.Common.Interfaces;
using FocusAI.Application.Common.Mappings;
using FocusAI.Domain.Audio;
using FocusAI.Domain.Speech;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FocusAI.Application.Features.Stories;

/// <summary>The rendered reading, or nothing.</summary>
public sealed record StoryAudio(byte[] Audio, string ContentType, string FileName);

/// <summary>
/// A story read aloud, as a file the browser can play.
/// </summary>
/// <remarks>
/// Separate from <see cref="GetStorySpeechQuery"/>, which returns the script for
/// the device's own voice. Both exist because both are needed: this one sounds far
/// better and can keep playing with the screen locked, and the other one works
/// when there is no key, no quota left, or no network worth spending.
///
/// Returns null rather than throwing when synthesis is unavailable. The endpoint
/// turns that into a 404 and the player falls back — a reader whose allowance ran
/// out mid-morning gets a different voice, not a broken button.
/// </remarks>
public sealed record GetStoryAudioQuery(string Slug, string? Voice = null) : IRequest<StoryAudio?>;

public sealed class GetStoryAudioQueryHandler(
    IApplicationDbContext db,
    ISpeechSynthesizer synthesizer,
    ISpeechAudioCache cache) : IRequestHandler<GetStoryAudioQuery, StoryAudio?>
{
    public async Task<StoryAudio?> Handle(GetStoryAudioQuery request, CancellationToken cancellationToken)
    {
        if (!synthesizer.IsEnabled)
        {
            return null;
        }

        var slug = request.Slug.Trim().ToLowerInvariant();

        var story = await db.Stories
            .AsNoTracking()
            .Where(StoryFilters.VisibleToReaders)
            .Where(s => s.Slug == slug)
            .Select(s => new
            {
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

        // The same script the device voice reads, so switching engines changes how
        // the story sounds and not what it says.
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

        var script = string.Join('\n', chunks.Select(chunk => chunk.Text)).Trim();
        if (script.Length == 0)
        {
            return null;
        }

        var voice = string.IsNullOrWhiteSpace(request.Voice) ? synthesizer.DefaultVoice : request.Voice;
        var key = CacheKey(story.Slug, voice, script);

        var audio = await cache.GetOrCreateAsync(
            key,
            async token => (await synthesizer.SynthesizeAsync(script, voice, token))?.Audio,
            cancellationToken);

        return audio is null
            ? null
            : new StoryAudio(audio, WavWriter.ContentType, $"{story.Slug}.wav");
    }

    /// <summary>
    /// Slug, voice, and a digest of the script itself.
    /// </summary>
    /// <remarks>
    /// The script hash is the part that matters. Re-enrichment rewrites a story's
    /// summary in place, and without it the reader would keep hearing the old
    /// wording read over a story that no longer says it — cached for ever, because
    /// nothing about the slug changed. Hashing the text means a rewritten story is
    /// simply a different reading, and the stale one ages out on its own.
    /// </remarks>
    private static string CacheKey(string slug, string voice, string script)
    {
        var digest = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(script)))[..16].ToLowerInvariant();

        // The slug is already URL-safe; the voice is from a fixed list. Neither can
        // reach outside the cache directory.
        return $"{slug}.{voice.ToLowerInvariant()}.{digest}";
    }
}
