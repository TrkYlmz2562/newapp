using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FocusAI.Application.Common.Interfaces;
using FocusAI.Domain.Audio;
using FocusAI.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FocusAI.Infrastructure.Ai;

/// <summary>
/// Reads a story aloud with Gemini's speech models.
/// </summary>
/// <remarks>
/// Chosen because the key was already in the file. The alternative engines all
/// wanted a second account and a second bill for a feature that may or may not get
/// used, and the free allowance here is generous enough for one reader's day:
/// measured on the project's own dashboard, a hundred renderings and ten thousand
/// tokens a minute. That is roughly three stories a minute, which is faster than
/// anyone listens, and a hundred new stories a day, which is more than the feed
/// produces — but only because every rendering is cached and re-listening costs
/// nothing. Generating audio in the ingestion pipeline instead would spend the
/// day's whole allowance before breakfast, on stories nobody asked to hear.
///
/// Everything here degrades to null rather than throwing. The device's own voice
/// is still wired up behind this, and a reader whose quota ran out should notice
/// a change in voice, not a broken button.
/// </remarks>
public sealed class GeminiSpeechSynthesizer(
    IHttpClientFactory httpClientFactory,
    IOptions<SpeechOptions> options,
    ILogger<GeminiSpeechSynthesizer> logger) : ISpeechSynthesizer
{
    /// <summary>
    /// The prebuilt voices the API accepts. Not every one speaks every language
    /// equally well; the default is set in configuration, not here.
    /// </summary>
    private static readonly string[] PrebuiltVoices =
    [
        "Zephyr", "Puck", "Charon", "Kore", "Fenrir", "Leda", "Orus", "Aoede",
        "Callirrhoe", "Autonoe", "Enceladus", "Iapetus", "Umbriel", "Algieba",
        "Despina", "Erinome", "Algenib", "Rasalgethi", "Laomedeia", "Achernar",
        "Alnilam", "Schedar", "Gacrux", "Pulcherrima", "Achird", "Zubenelgenubi",
        "Vindemiatrix", "Sadachbia", "Sadaltager", "Sulafat"
    ];

    /// <summary>
    /// When the provider last said no, as UTC ticks. Static because the refusal is
    /// about the account, not about this request or this scope.
    /// </summary>
    private static long _quotaSpentUntil;

    private SpeechOptions Options => options.Value;

    /// <summary>True while a recent 429 is still worth respecting.</summary>
    private static bool InCooldown => Interlocked.Read(ref _quotaSpentUntil) > DateTime.UtcNow.Ticks;

    public bool IsEnabled => Options.Enabled && !string.IsNullOrWhiteSpace(Options.ApiKey);

    public IReadOnlyList<string> Voices => IsEnabled ? PrebuiltVoices : [];

    public string DefaultVoice =>
        PrebuiltVoices.Contains(Options.Voice, StringComparer.OrdinalIgnoreCase)
            ? Options.Voice
            : "Kore";

    public async Task<SynthesizedSpeech?> SynthesizeAsync(
        string text,
        string? voice = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsEnabled || string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        // The allowance was spent a moment ago and nothing since then can have
        // refilled it. Asking again would buy the reader another wait for the same
        // refusal — the queue behind this story would pay it once each.
        if (InCooldown)
        {
            return null;
        }

        // A hard ceiling well under the model's 32k-token session limit. A script
        // longer than this is a bug upstream, and finding out by spending the
        // day's quota on it is an expensive way to learn.
        if (text.Length > Options.MaxCharacters)
        {
            logger.LogWarning(
                "FocusAI speech skipped a {Length}-character script; the ceiling is {Max}",
                text.Length,
                Options.MaxCharacters);

            return null;
        }

        var chosen = PrebuiltVoices.FirstOrDefault(
            v => string.Equals(v, voice, StringComparison.OrdinalIgnoreCase)) ?? DefaultVoice;

        try
        {
            var client = httpClientFactory.CreateClient(SpeechOptions.HttpClientName);

            using var response = await client.PostAsJsonAsync(
                $"v1beta/models/{Options.Model}:generateContent?key={Uri.EscapeDataString(Options.ApiKey)}",
                new
                {
                    contents = new[] { new { parts = new[] { new { text } } } },
                    generationConfig = new
                    {
                        responseModalities = new[] { "AUDIO" },
                        speechConfig = new
                        {
                            voiceConfig = new { prebuiltVoiceConfig = new { voiceName = chosen } }
                        }
                    }
                },
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    var cooldown = TimeSpan.FromSeconds(Math.Clamp(Options.QuotaCooldownSeconds, 5, 3600));
                    Interlocked.Exchange(ref _quotaSpentUntil, DateTime.UtcNow.Add(cooldown).Ticks);

                    // The expected end of a free day rather than a fault, so it is
                    // said plainly instead of as an error with a stack trace.
                    logger.LogInformation(
                        "FocusAI speech quota is spent; using the device voice for the next {Seconds}s",
                        (int)cooldown.TotalSeconds);
                }
                else
                {
                    logger.LogWarning(
                        "FocusAI speech synthesis returned {Status}; falling back to the device voice",
                        (int)response.StatusCode);
                }

                return null;
            }

            await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var json = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);

            var encoded = ExtractAudio(json.RootElement);
            if (encoded is null)
            {
                // Documented failure mode: the API answers 200 with usage metadata
                // and no audio part when it declines to speak the text.
                logger.LogWarning("FocusAI speech synthesis returned no audio payload");
                return null;
            }

            var pcm = Convert.FromBase64String(encoded);
            if (pcm.Length == 0)
            {
                return null;
            }

            // Headerless PCM in, playable file out — see WavWriter.
            var wav = WavWriter.IsWav(pcm) ? pcm : WavWriter.FromPcm(pcm, Options.SampleRate);

            return new SynthesizedSpeech(wav, WavWriter.ContentType, chosen, Options.Model);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "FocusAI speech synthesis failed; falling back to the device voice");
            return null;
        }
    }

    /// <summary>
    /// Digs the base64 audio out of candidates[0].content.parts[*].inlineData.data.
    /// </summary>
    /// <remarks>
    /// Walks the parts rather than taking the first, because a response may carry a
    /// text part alongside the audio and their order is not promised.
    /// </remarks>
    private static string? ExtractAudio(JsonElement root)
    {
        if (!root.TryGetProperty("candidates", out var candidates) ||
            candidates.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var candidate in candidates.EnumerateArray())
        {
            if (!candidate.TryGetProperty("content", out var content) ||
                !content.TryGetProperty("parts", out var parts) ||
                parts.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var part in parts.EnumerateArray())
            {
                if (part.TryGetProperty("inlineData", out var inline) &&
                    inline.TryGetProperty("data", out var data) &&
                    data.ValueKind == JsonValueKind.String &&
                    data.GetString() is { Length: > 0 } encoded)
                {
                    return encoded;
                }
            }
        }

        return null;
    }
}
