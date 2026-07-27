namespace FocusAI.Application.Common.Interfaces;

/// <summary>One rendered reading, ready to be served as a file.</summary>
public sealed record SynthesizedSpeech(
    byte[] Audio,
    string ContentType,
    string Voice,
    string Model);

/// <summary>
/// Turning a story's script into audio the browser can play as a file.
/// </summary>
/// <remarks>
/// A port rather than a direct call to a provider, and not because ports are
/// tidy. The provider here is a preview model that may be renamed or withdrawn,
/// its free quota is small enough to run out on an ordinary day, and the device's
/// own voice has to keep working when either happens. The caller asks for audio
/// and gets null when there is none; what it does about that — fall back to the
/// browser's speech synthesis — is the same answer whichever reason applies.
/// </remarks>
public interface ISpeechSynthesizer
{
    /// <summary>False when no key is configured, so callers can skip the round trip.</summary>
    bool IsEnabled { get; }

    /// <summary>Voice names the provider accepts. Empty when disabled.</summary>
    IReadOnlyList<string> Voices { get; }

    string DefaultVoice { get; }

    /// <summary>
    /// Renders <paramref name="text"/>, or returns null when the provider is
    /// unavailable, out of quota, or gave back something unusable.
    /// </summary>
    Task<SynthesizedSpeech?> SynthesizeAsync(
        string text,
        string? voice = null,
        CancellationToken cancellationToken = default);
}
