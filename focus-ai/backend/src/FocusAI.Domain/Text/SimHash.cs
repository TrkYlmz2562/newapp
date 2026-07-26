using System.Text;

namespace FocusAI.Domain.Text;

/// <summary>
/// 64-bit SimHash over word shingles (Charikar). This is the cheap first pass of
/// the de-duplication stage: a Hamming-distance scan over a bigint column
/// discards the overwhelming majority of non-duplicate pairs before any
/// embedding is loaded.
/// </summary>
public static class SimHash
{
    private const int Bits = 64;

    public static long Compute(string? text)
    {
        var vector = new int[Bits];
        var any = false;

        foreach (var shingle in TextNormalizer.Shingles(text))
        {
            any = true;
            var hash = Fnv1A64(shingle);

            for (var bit = 0; bit < Bits; bit++)
            {
                var isSet = (hash >> bit & 1UL) == 1UL;
                vector[bit] += isSet ? 1 : -1;
            }
        }

        if (!any)
        {
            return 0L;
        }

        var result = 0UL;
        for (var bit = 0; bit < Bits; bit++)
        {
            if (vector[bit] > 0)
            {
                result |= 1UL << bit;
            }
        }

        // Postgres has no unsigned 64-bit type, so the value round-trips as bigint.
        return unchecked((long)result);
    }

    /// <summary>Number of differing bits. 0 means "almost certainly the same text".</summary>
    public static int HammingDistance(long left, long right) =>
        System.Numerics.BitOperations.PopCount(unchecked((ulong)(left ^ right)));

    /// <summary>
    /// Default near-duplicate threshold, in differing bits out of 64.
    /// </summary>
    /// <remarks>
    /// Measured on real headlines from the seeded sources: a single substituted
    /// word in a ~15-word headline lands at 7-9 bits, a substantially reworded
    /// account of the same event at ~19, and unrelated stories at 28-31 (random
    /// text averages 32). 10 sits in the gap: it catches syndicated copies with
    /// light edits, and deliberately leaves reworded-but-same-event coverage to
    /// the embedding stage, which is the tool that can actually judge meaning.
    /// </remarks>
    public const int DefaultNearDuplicateThreshold = 10;

    /// <summary>
    /// True when two texts are close enough to be the same article. A zero hash
    /// means "no extractable text" and never matches — otherwise every failed
    /// extraction would collapse into a single story.
    /// </summary>
    public static bool IsNearDuplicate(long left, long right, int threshold = DefaultNearDuplicateThreshold) =>
        left != 0L && right != 0L && HammingDistance(left, right) <= threshold;

    private static ulong Fnv1A64(string value)
    {
        const ulong offsetBasis = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;

        var hash = offsetBasis;
        foreach (var b in Encoding.UTF8.GetBytes(value))
        {
            hash ^= b;
            hash *= prime;
        }

        return hash;
    }
}
