using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Pgvector;

namespace FocusAI.Infrastructure.Persistence;

/// <summary>
/// Bridges the domain's provider-agnostic <c>float[]</c> embeddings to the
/// pgvector column type.
/// </summary>
/// <remarks>
/// The dimension is fixed at model-build time because pgvector columns are
/// typed <c>vector(N)</c>. Every <see cref="Application.Common.Interfaces.IEmbeddingService"/>
/// implementation must therefore emit exactly <see cref="Dimensions"/> floats;
/// changing this value requires a migration and a full re-embed.
/// </remarks>
public static class EmbeddingConfig
{
    /// <summary>Matches OpenAI text-embedding-3-small and is under pgvector's index limit.</summary>
    public const int Dimensions = 1536;

    public static string ColumnType => $"vector({Dimensions})";

    public static readonly ValueConverter<float[]?, Vector?> Converter = new(
        value => value == null ? null : new Vector(value),
        value => value == null ? null : value.ToArray());

    /// <summary>
    /// Without an explicit comparer EF treats the array reference as the identity
    /// and misses in-place mutations, so a recomputed centroid would never persist.
    /// </summary>
    public static readonly ValueComparer<float[]?> Comparer = new(
        (left, right) => left == null ? right == null : right != null && left.SequenceEqual(right),
        value => value == null ? 0 : value.Aggregate(17, (hash, item) => HashCode.Combine(hash, item)),
        value => value == null ? null : value.ToArray());
}
