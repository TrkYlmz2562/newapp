namespace FocusAI.Domain.Text;

/// <summary>URL-safe slugs for stories, topics and sources.</summary>
public static class Slugger
{
    public static string Slugify(string? value, int maxLength = 80)
    {
        var normalized = TextNormalizer.Normalize(value);
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        var slug = normalized.Replace(' ', '-');

        if (slug.Length <= maxLength)
        {
            return slug;
        }

        // Cut on a word boundary so slugs never end mid-token.
        var truncated = slug[..maxLength];
        var lastDash = truncated.LastIndexOf('-');
        return lastDash > maxLength / 2 ? truncated[..lastDash] : truncated;
    }

    /// <summary>
    /// Appends a short discriminator so two stories published the same day with
    /// the same headline do not collide on the unique slug index.
    /// </summary>
    /// <remarks>
    /// The suffix is taken from the <em>end</em> of the GUID, not the start.
    /// UUIDv7 — which <see cref="Common.BaseEntity"/> generates — puts a 48-bit
    /// timestamp in the leading bytes, so a leading slice is effectively
    /// constant for everything created in the same few hours and provides no
    /// discrimination at all. The trailing bytes are the random ones.
    /// </remarks>
    public static string SlugifyUnique(string? value, Guid id, int maxLength = 72)
    {
        var baseSlug = Slugify(value, maxLength);
        var hex = id.ToString("N");
        var suffix = hex[^8..];
        return baseSlug.Length == 0 ? suffix : $"{baseSlug}-{suffix}";
    }
}
