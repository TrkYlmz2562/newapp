using System.Collections.Concurrent;
using FocusAI.Application.Common.Interfaces;
using FocusAI.Infrastructure.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FocusAI.Infrastructure.Speech;

/// <summary>
/// Rendered readings, on disk.
/// </summary>
/// <remarks>
/// Files rather than rows: these are hundreds of kilobytes each, they are served
/// verbatim, and nothing ever queries them by anything but their key. Putting them
/// in Postgres would mean loading them through the connection to hand straight to
/// a socket.
///
/// Nothing evicts them. A reading is a few hundred kilobytes and the feed produces
/// tens of stories a day, so a year of listening is measured in gigabytes — and the
/// cost of dropping one is a rendering from an allowance of a hundred a day. If
/// that ever changes the eviction rule wants to be "oldest first", but adding it
/// now would be inventing a problem.
/// </remarks>
public sealed class FileSpeechAudioCache : ISpeechAudioCache
{
    /// <summary>
    /// One gate per key, so two readers asking for the same story at the same
    /// moment produce one rendering rather than two.
    /// </summary>
    /// <remarks>
    /// Per-key rather than one global lock: a single gate would serialise every
    /// rendering in the process, and the queue player deliberately renders the next
    /// story while the current one is still playing.
    /// </remarks>
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.Ordinal);

    private readonly string _directory;
    private readonly ILogger<FileSpeechAudioCache> _logger;

    public FileSpeechAudioCache(
        IOptions<SpeechOptions> options,
        IHostEnvironment environment,
        ILogger<FileSpeechAudioCache> logger)
    {
        _logger = logger;

        var configured = string.IsNullOrWhiteSpace(options.Value.CacheDirectory)
            ? "speech-cache"
            : options.Value.CacheDirectory;

        _directory = Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(environment.ContentRootPath, configured);
    }

    public async Task<byte[]?> GetOrCreateAsync(
        string key,
        Func<CancellationToken, Task<byte[]?>> create,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(create);

        var path = PathFor(key);

        var cached = await TryReadAsync(path, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        var gate = Gates.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);

        try
        {
            // Re-checked inside the gate: the request that was waiting is exactly
            // the one whose work has just been done for it.
            cached = await TryReadAsync(path, cancellationToken);
            if (cached is not null)
            {
                return cached;
            }

            var created = await create(cancellationToken);

            // Null means "not now" — no provider, no quota, no usable answer. Caching
            // that would turn a bad afternoon into a permanent absence.
            if (created is not { Length: > 0 })
            {
                return null;
            }

            await WriteAsync(path, created, cancellationToken);
            return created;
        }
        finally
        {
            gate.Release();

            // Only drop the gate when nobody is queued behind it, or a waiter would
            // be left holding a semaphore no new arrival can find.
            if (gate.CurrentCount == 1)
            {
                Gates.TryRemove(new KeyValuePair<string, SemaphoreSlim>(key, gate));
            }
        }
    }

    private string PathFor(string key) => Path.Combine(_directory, $"{key}.wav");

    private async Task<byte[]?> TryReadAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            return File.Exists(path) ? await File.ReadAllBytesAsync(path, cancellationToken) : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // An unreadable cache entry is a slow day, not a failure: the caller
            // renders it again.
            _logger.LogWarning(ex, "FocusAI could not read cached speech at {Path}", path);
            return null;
        }
    }

    private async Task WriteAsync(string path, byte[] audio, CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(_directory);

            // Written beside the target and moved into place, so a reader can never
            // open a file that is still being written.
            var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
            await File.WriteAllBytesAsync(temporary, audio, cancellationToken);
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The audio is already rendered and about to be served. Failing to keep
            // a copy costs one rendering next time; throwing would cost the listen.
            _logger.LogWarning(ex, "FocusAI could not cache speech at {Path}", path);
        }
    }
}
