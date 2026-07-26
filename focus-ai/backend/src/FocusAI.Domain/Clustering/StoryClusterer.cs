using FocusAI.Domain.Text;

namespace FocusAI.Domain.Clustering;

/// <summary>Minimal projection of an article needed to decide cluster membership.</summary>
public sealed record ClusterCandidate
{
    public required Guid ArticleId { get; init; }

    public required Guid SourceId { get; init; }

    public required string ContentHash { get; init; }

    public required long SimHash { get; init; }

    public float[]? Embedding { get; init; }

    public required DateTimeOffset PublishedAt { get; init; }
}

/// <summary>Existing cluster an incoming article might join.</summary>
public sealed record ClusterTarget
{
    public required Guid StoryId { get; init; }

    public required long SimHash { get; init; }

    public float[]? Embedding { get; init; }

    public required DateTimeOffset PublishedAt { get; init; }

    public required DateTimeOffset LastActivityAt { get; init; }

    public IReadOnlySet<string> ContentHashes { get; init; } = new HashSet<string>();
}

public enum ClusterMatchKind
{
    /// <summary>No existing story is close enough; open a new one.</summary>
    None = 0,
    /// <summary>Byte-identical body — a straight repost.</summary>
    ExactHash = 1,
    /// <summary>Near-identical text — syndicated copy.</summary>
    NearDuplicate = 2,
    /// <summary>Semantically the same event covered independently.</summary>
    Semantic = 3
}

public sealed record ClusterDecision(ClusterMatchKind Kind, Guid? StoryId, double Similarity)
{
    public static readonly ClusterDecision NoMatch = new(ClusterMatchKind.None, null, 0d);

    public bool IsMatch => Kind != ClusterMatchKind.None;
}

/// <summary>
/// The "Kaynak Birleştirme" stage of PRD sections 5.3 and 13. Three escalating
/// tests, cheapest first: exact hash, then SimHash Hamming distance, then
/// cosine similarity on embeddings.
/// </summary>
public static class StoryClusterer
{
    /// <summary>
    /// Cosine similarity above which two independently-written pieces are treated
    /// as the same event. Tuned high: over-merging distinct stories is a far worse
    /// failure than showing two cards, because the merged card hides one of them.
    /// </summary>
    public const double DefaultSemanticThreshold = 0.86;

    /// <summary>
    /// Coverage of one event trails in over days. Beyond this window the same
    /// topic is a follow-up story, not the original.
    /// </summary>
    public static readonly TimeSpan DefaultWindow = TimeSpan.FromHours(72);

    public static ClusterDecision FindCluster(
        ClusterCandidate candidate,
        IEnumerable<ClusterTarget> targets,
        double semanticThreshold = DefaultSemanticThreshold,
        TimeSpan? window = null)
    {
        var effectiveWindow = window ?? DefaultWindow;
        var best = ClusterDecision.NoMatch;

        foreach (var target in targets)
        {
            if (!WithinWindow(candidate.PublishedAt, target, effectiveWindow))
            {
                continue;
            }

            if (target.ContentHashes.Contains(candidate.ContentHash))
            {
                // Exact match is conclusive; nothing later can beat it.
                return new ClusterDecision(ClusterMatchKind.ExactHash, target.StoryId, 1d);
            }

            if (SimHash.IsNearDuplicate(candidate.SimHash, target.SimHash))
            {
                var distance = SimHash.HammingDistance(candidate.SimHash, target.SimHash);
                var similarity = 1d - distance / 64d;
                if (similarity > best.Similarity)
                {
                    best = new ClusterDecision(ClusterMatchKind.NearDuplicate, target.StoryId, similarity);
                }

                continue;
            }

            var cosine = VectorMath.CosineSimilarity(candidate.Embedding, target.Embedding);
            if (cosine >= semanticThreshold && cosine > best.Similarity)
            {
                best = new ClusterDecision(ClusterMatchKind.Semantic, target.StoryId, cosine);
            }
        }

        return best;
    }

    /// <summary>
    /// An article belongs to a cluster if it falls within the window of either
    /// the story's original publication or its most recent activity — long-running
    /// stories keep absorbing coverage without the window sliding indefinitely
    /// from the original date.
    /// </summary>
    private static bool WithinWindow(DateTimeOffset publishedAt, ClusterTarget target, TimeSpan window)
    {
        var fromStart = (publishedAt - target.PublishedAt).Duration();
        var fromActivity = (publishedAt - target.LastActivityAt).Duration();
        return fromStart <= window || fromActivity <= window;
    }
}
