using FocusAI.Domain.Text;
using Xunit;

namespace FocusAI.UnitTests.Text;

public class TextNormalizerTests
{
    [Theory]
    [InlineData("GPT-6 Yayınlandı!", "gpt 6 yayinlandi")]
    [InlineData("  .NET   9   Released  ", "net 9 released")]
    [InlineData("Açık Kaynak Güncelleme", "acik kaynak guncelleme")]
    [InlineData("Straße", "strase")]
    [InlineData("Dil Modelleri İçin", "dil modelleri icin")]
    [InlineData("İSTANBUL", "istanbul")]
    public void Normalize_strips_case_diacritics_and_punctuation(string input, string expected) =>
        Assert.Equal(expected, TextNormalizer.Normalize(input));

    [Fact]
    public void Normalize_leaves_nothing_uppercase_behind()
    {
        // ToLowerInvariant does not touch 'İ' (U+0130), and FormD then splits it
        // into 'I' + combining dot. Strip the dot as a diacritic and a capital 'I'
        // walks out of a method that promises lowercase.
        var normalized = TextNormalizer.Normalize("VIGOR: Dil Modelleri İçin Varyans Odaklı Çıkarım");

        Assert.Equal(normalized.ToLowerInvariant(), normalized);
        Assert.DoesNotContain('I', normalized);
    }

    [Fact]
    public void Turkish_stop_words_are_written_the_way_normalisation_leaves_them()
    {
        // "için" could never match a normalised token — the cedilla is gone by the
        // time the list is consulted — so the entry sat in the list doing nothing.
        Assert.DoesNotContain("icin", TextNormalizer.Tokenize("Yeni model Türkçe için eğitildi"));
    }

    [Fact]
    public void Normalize_handles_null_and_whitespace() =>
        Assert.Equal(string.Empty, TextNormalizer.Normalize(null));

    [Fact]
    public void Turkish_dotted_and_dotless_i_normalize_together()
    {
        // Intentional: it makes "Yayınlandı" and "Yayinlandi" collide, which is
        // what de-duplication across Turkish sources needs.
        Assert.Equal(TextNormalizer.Normalize("ışık"), TextNormalizer.Normalize("isik"));
    }

    [Fact]
    public void Tokenize_drops_stop_words_and_single_characters()
    {
        var tokens = TextNormalizer.Tokenize("The new version of the framework is a big release");

        Assert.DoesNotContain("the", tokens);
        Assert.DoesNotContain("is", tokens);
        Assert.Contains("framework", tokens);
        Assert.Contains("release", tokens);
    }

    [Fact]
    public void Shingles_are_overlapping_pairs()
    {
        var shingles = TextNormalizer.Shingles("dotnet nine release notes").ToList();

        Assert.Equal(["dotnet nine", "nine release", "release notes"], shingles);
    }

    [Fact]
    public void Shingles_of_a_single_token_return_that_token()
    {
        var shingles = TextNormalizer.Shingles("kubernetes").ToList();

        Assert.Single(shingles);
        Assert.Equal("kubernetes", shingles[0]);
    }
}

public class SimHashTests
{
    [Fact]
    public void Identical_text_produces_identical_hashes() =>
        Assert.Equal(
            SimHash.Compute("OpenAI releases GPT-6 today"),
            SimHash.Compute("OpenAI releases GPT-6 today"));

    [Fact]
    public void Punctuation_and_case_do_not_change_the_hash() =>
        Assert.Equal(
            SimHash.Compute("OpenAI releases GPT-6 today!"),
            SimHash.Compute("openai   RELEASES gpt 6 today"));

    [Fact]
    public void Similar_text_is_closer_than_unrelated_text()
    {
        var original = SimHash.Compute(
            "Microsoft ships .NET 10 with native AOT improvements and faster startup");
        var similar = SimHash.Compute(
            "Microsoft ships .NET 10 with native AOT improvements and quicker startup");
        var unrelated = SimHash.Compute(
            "Researchers publish a new paper on protein folding in Nature this week");

        Assert.True(
            SimHash.HammingDistance(original, similar) <
            SimHash.HammingDistance(original, unrelated));
    }

    [Fact]
    public void Empty_input_hashes_to_zero_and_never_counts_as_a_duplicate()
    {
        Assert.Equal(0L, SimHash.Compute(""));
        Assert.False(SimHash.IsNearDuplicate(0L, 0L));
    }
}

public class UrlNormalizerTests
{
    [Theory]
    [InlineData("https://example.com/post?utm_source=x&utm_medium=y", "https://example.com/post")]
    [InlineData("http://www.example.com/post/", "https://example.com/post")]
    [InlineData("https://Example.COM/Post#section", "https://example.com/Post")]
    [InlineData("https://example.com/post?id=5&utm_campaign=z", "https://example.com/post?id=5")]
    [InlineData("https://example.com/post?fbclid=abc", "https://example.com/post")]
    public void Tracking_noise_is_removed(string input, string expected) =>
        Assert.Equal(expected, UrlNormalizer.Normalize(input));

    [Fact]
    public void Meaningful_query_parameters_survive_and_are_ordered()
    {
        // Stable ordering is what lets two orderings of the same URL hash alike.
        Assert.Equal(
            "https://example.com/search?page=2&q=dotnet",
            UrlNormalizer.Normalize("https://example.com/search?q=dotnet&page=2"));
    }

    [Fact]
    public void Same_article_from_two_aggregators_normalizes_identically()
    {
        var fromTwitter = UrlNormalizer.Normalize("https://blog.dev/post?utm_source=twitter");
        var fromNewsletter = UrlNormalizer.Normalize("http://www.blog.dev/post/?utm_source=newsletter&mc_cid=1");

        Assert.Equal(fromTwitter, fromNewsletter);
    }

    [Fact]
    public void Non_absolute_input_is_returned_unchanged_rather_than_dropped() =>
        Assert.Equal("not a url", UrlNormalizer.Normalize("not a url"));
}

public class SluggerTests
{
    [Fact]
    public void Slug_is_url_safe_and_lowercase() =>
        Assert.Equal("gpt-6-yayinlandi", Slugger.Slugify("GPT-6 Yayınlandı!"));

    [Fact]
    public void Turkish_dotted_capital_i_does_not_leave_an_uppercase_letter_in_the_slug()
    {
        // The bug this guards: "…-modelleri-Icin-varyans-…". Slug lookups lowercase
        // the slug they are given before matching, so a stored slug carrying a
        // capital letter can never be found — the story 404s on its own URL, for
        // every link that points at it, forever.
        var slug = Slugger.Slugify("VIGOR: Dil Modelleri İçin Varyans Odaklı Çıkarım Tahsis Yöntemi");

        Assert.Equal("vigor-dil-modelleri-icin-varyans-odakli-cikarim-tahsis-yontemi", slug);
        Assert.Equal(slug.ToLowerInvariant(), slug);
    }

    [Fact]
    public void Every_slug_survives_being_lowercased_by_the_lookup()
    {
        string[] headlines =
        [
            "İnternet Altyapısı Değişiyor",
            "TÜRKİYE'DE YAPAY ZEKÂ",
            "Şirket İçin Yeni Ürün",
            "Iğdır'da Çığır Açan Buluş"
        ];

        foreach (var headline in headlines)
        {
            var slug = Slugger.SlugifyUnique(headline, Guid.CreateVersion7());
            Assert.Equal(slug.ToLowerInvariant(), slug);
        }
    }

    [Fact]
    public void Long_titles_are_cut_on_a_word_boundary()
    {
        var slug = Slugger.Slugify(string.Join(' ', Enumerable.Repeat("kubernetes", 20)), maxLength: 50);

        Assert.True(slug.Length <= 50);
        Assert.False(slug.EndsWith('-'));
    }

    [Fact]
    public void Unique_suffix_differs_for_uuid_v7_values_created_together()
    {
        // Regression guard: an earlier version sliced the *leading* bytes of a
        // UUIDv7, which are a timestamp — so every slug minted in the same batch
        // collided on the unique index.
        var slugs = Enumerable.Range(0, 200)
            .Select(_ => Slugger.SlugifyUnique("Aynı başlık", Guid.CreateVersion7()))
            .ToHashSet();

        Assert.True(slugs.Count > 190, $"Expected near-unique slugs, got {slugs.Count} distinct of 200.");
    }

    [Fact]
    public void Empty_title_still_yields_a_usable_slug()
    {
        var slug = Slugger.SlugifyUnique("", Guid.NewGuid());

        Assert.False(string.IsNullOrWhiteSpace(slug));
    }
}

public class VectorMathTests
{
    [Fact]
    public void Cosine_of_identical_vectors_is_one() =>
        Assert.Equal(1d, VectorMath.CosineSimilarity([1f, 2f, 3f], [1f, 2f, 3f]), 5);

    [Fact]
    public void Cosine_of_orthogonal_vectors_is_zero() =>
        Assert.Equal(0d, VectorMath.CosineSimilarity([1f, 0f], [0f, 1f]), 5);

    [Theory]
    [InlineData(null)]
    [InlineData(new float[0])]
    public void Cosine_handles_degenerate_input(float[]? vector) =>
        Assert.Equal(0d, VectorMath.CosineSimilarity(vector, [1f, 2f]));

    [Fact]
    public void Cosine_of_mismatched_lengths_is_zero_rather_than_throwing() =>
        Assert.Equal(0d, VectorMath.CosineSimilarity([1f, 2f], [1f, 2f, 3f]));

    [Fact]
    public void Centroid_averages_component_wise() =>
        Assert.Equal(new[] { 2f, 3f }, VectorMath.Centroid([[1f, 2f], [3f, 4f]]));

    [Fact]
    public void Centroid_refuses_mixed_dimensions()
    {
        // Mixing embedding models within one cluster would silently produce
        // meaningless vectors; returning null forces the caller to skip instead.
        Assert.Null(VectorMath.Centroid([[1f, 2f], [1f, 2f, 3f]]));
    }

    [Fact]
    public void Normalize_produces_unit_length()
    {
        var normalized = VectorMath.Normalize([3f, 4f]);
        var magnitude = Math.Sqrt(normalized.Sum(v => v * (double)v));

        Assert.Equal(1d, magnitude, 5);
    }

    [Fact]
    public void Normalize_leaves_a_zero_vector_alone() =>
        Assert.Equal(new[] { 0f, 0f }, VectorMath.Normalize([0f, 0f]));
}
