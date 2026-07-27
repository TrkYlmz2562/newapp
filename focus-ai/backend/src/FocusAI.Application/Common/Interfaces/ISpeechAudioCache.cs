namespace FocusAI.Application.Common.Interfaces;

/// <summary>
/// Keeps rendered readings so a story is only ever spoken once.
/// </summary>
/// <remarks>
/// The daily allowance is counted in renderings, not in listens, so the cache is
/// what makes the feature affordable rather than what makes it fast. A reader who
/// plays the same story three times, or two readers who play it once each, must
/// cost one rendering between them.
///
/// Single-flight for the same reason: "günü dinle" and the story page can ask for
/// the same audio within a second of each other, and two calls would spend two of
/// a hundred to produce identical bytes.
/// </remarks>
public interface ISpeechAudioCache
{
    /// <summary>
    /// Returns the cached audio for <paramref name="key"/>, calling
    /// <paramref name="create"/> exactly once if it is not there yet.
    /// </summary>
    /// <remarks>
    /// A null from <paramref name="create"/> is not cached: it means the provider
    /// was unavailable or out of quota, and tomorrow's attempt should be allowed to
    /// succeed.
    /// </remarks>
    Task<byte[]?> GetOrCreateAsync(
        string key,
        Func<CancellationToken, Task<byte[]?>> create,
        CancellationToken cancellationToken = default);
}
