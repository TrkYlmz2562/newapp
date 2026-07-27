using FocusAI.Domain.Speech;
using Xunit;

namespace FocusAI.UnitTests.Speech;

public class SpeechScriptTests
{
    private static string OneSentence(string text) => Assert.Single(SpeechScript.Sentences(text));

    // ── Sentence splitting ────────────────────────────────────────────────

    [Fact]
    public void Plain_sentences_split_on_the_terminator()
    {
        var result = SpeechScript.Sentences("Model yayımlandı. Fiyat değişmedi.");

        Assert.Equal(["Model yayımlandı.", "Fiyat değişmedi."], result);
    }

    [Theory]
    [InlineData("Sürüm 3.5 yayımlandı.")]
    [InlineData("Fiyat 1.250 lira oldu.")]
    public void A_number_is_not_split_at_its_decimal_point(string text) =>
        Assert.Single(SpeechScript.Sentences(text));

    [Fact]
    public void A_Turkish_ordinal_does_not_end_a_sentence()
    {
        // "3. çeyrek" is the normal way to write a quarter, and splitting there
        // drops a pause into the middle of the phrase.
        var result = OneSentence("Yatırım 3. çeyrekte tamamlanacak.");

        Assert.Equal("Yatırım 3. çeyrekte tamamlanacak.", result);
    }

    [Fact]
    public void An_ordinal_followed_by_a_capitalised_word_still_does_not_split() =>
        Assert.Single(SpeechScript.Sentences("Toplantı 2. Salon'da yapılacak."));

    [Theory]
    [InlineData("Dr. Yılmaz açıklama yaptı.")]
    [InlineData("Sunucu, veritabanı vs. hepsi güncellendi.")]
    [InlineData("Bkz. ilgili doküman.")]
    public void Known_abbreviations_do_not_end_a_sentence(string text) =>
        Assert.Single(SpeechScript.Sentences(text));

    [Fact]
    public void An_initial_does_not_end_a_sentence() =>
        Assert.Single(SpeechScript.Sentences("Rapor A. Kaya tarafından yazıldı."));

    [Fact]
    public void A_dot_glued_to_the_next_word_is_inside_a_token() =>
        Assert.Single(SpeechScript.Sentences("Node.js sürümü güncellendi."));

    [Fact]
    public void A_Turkish_capital_I_starts_a_new_sentence()
    {
        // char.IsUpper handles İ; an A-Z range check would not, and the second
        // sentence would be swallowed into the first.
        var result = SpeechScript.Sentences("Model çıktı. İlk testler olumlu.");

        Assert.Equal(2, result.Count);
        Assert.Equal("İlk testler olumlu.", result[1]);
    }

    [Fact]
    public void A_question_and_an_exclamation_both_end_sentences() =>
        Assert.Equal(3, SpeechScript.Sentences("Ne değişti? Her şey. Gerçekten!").Count);

    // ── Written for the ear ───────────────────────────────────────────────

    [Fact]
    public void Percentages_are_read_the_Turkish_way_round() =>
        Assert.Contains("yüzde 40", OneSentence("Kullanım %40 arttı."));

    [Fact]
    public void A_scaled_amount_becomes_words() =>
        Assert.Contains("20 milyon dolar", OneSentence("Şirket 20M$ yatırım aldı."));

    [Fact]
    public void A_leading_currency_sign_is_read_after_the_number() =>
        Assert.Contains("1,5 milyar dolar", OneSentence("Anlaşma $1,5B seviyesinde."));

    [Fact]
    public void Units_are_read_as_words_not_letters() =>
        Assert.Contains("16 gigabayt", OneSentence("Cihazda 16GB bellek var."));

    [Fact]
    public void Acronyms_are_spelled_out_in_Turkish_letter_names()
    {
        var result = OneSentence("Yeni API yayımlandı.");

        Assert.Contains("ey pi ay", result);
        Assert.DoesNotContain("API", result);
    }

    [Fact]
    public void An_acronym_carrying_a_Turkish_suffix_is_still_expanded() =>
        Assert.Contains("ey pi ay'yi", OneSentence("API'yi güncelledik."));

    [Fact]
    public void A_word_that_merely_contains_an_acronym_is_left_alone() =>
        Assert.Contains("Kapı", OneSentence("Kapı açıldı."));

    [Fact]
    public void Brand_names_are_left_alone()
    {
        // Transliterating these is a guess about one engine's phonetics, and a
        // wrong guess sounds worse than the engine's own attempt.
        var result = OneSentence("GitHub ve Google duyurdu.");

        Assert.Contains("GitHub", result);
        Assert.Contains("Google", result);
    }

    [Fact]
    public void Urls_are_named_rather_than_read_aloud()
    {
        // Deleting the address outright leaves "Detaylar adresinde" — a sentence
        // whose subject has gone missing.
        var result = OneSentence("Detaylar https://example.com/a/b adresinde.");

        Assert.Equal("Detaylar bağlantıda.", result);
    }

    [Fact]
    public void A_url_with_no_trailing_locative_is_still_named() =>
        Assert.Equal("Bakınız bağlantıda.", OneSentence("Bkz. https://example.com/a/b."));

    [Fact]
    public void An_expanded_abbreviation_keeps_the_case_it_was_written_in()
    {
        // Lowercasing it would make the splitter read the next sentence as a
        // continuation of this one.
        Assert.StartsWith("Örneğin", OneSentence("Örn. yeni sürüm."));
        Assert.Contains("örneğin", OneSentence("Bir seçenek, örn. yeni sürüm."));
    }

    [Fact]
    public void Markdown_link_text_survives_and_the_target_does_not()
    {
        var result = OneSentence("[Resmî duyuru](https://x.dev/post) yayımlandı.");

        Assert.Contains("Resmî duyuru", result);
        Assert.DoesNotContain("x.dev", result);
    }

    [Fact]
    public void Decoration_is_stripped()
    {
        var result = OneSentence("**Önemli** — ▓ sistem › güncellendi.");

        Assert.DoesNotContain("*", result);
        Assert.DoesNotContain("▓", result);
        Assert.DoesNotContain("›", result);
        Assert.Contains("Önemli", result);
    }

    [Fact]
    public void Abbreviations_that_end_in_a_dot_are_expanded_before_splitting() =>
        Assert.Contains("ve benzeri", OneSentence("Sunucu, ağ vb. bileşenler."));

    // ── Utterance length ──────────────────────────────────────────────────

    [Fact]
    public void An_over_long_sentence_is_broken_at_a_comma()
    {
        var long_ = string.Join(", ", Enumerable.Repeat("bu bir cümle parçasıdır", 20)) + ".";

        var result = SpeechScript.Sentences(long_);

        Assert.True(result.Count > 1);
        Assert.All(result, sentence => Assert.True(sentence.Length <= 240, $"uzun kaldı: {sentence.Length}"));
    }

    [Fact]
    public void A_long_run_with_no_punctuation_is_still_broken_up()
    {
        var result = SpeechScript.Sentences(string.Join(' ', Enumerable.Repeat("kelime", 120)));

        Assert.True(result.Count > 1);
    }

    [Fact]
    public void Every_utterance_ends_in_punctuation_so_the_engine_pauses() =>
        Assert.All(
            SpeechScript.Sentences("Bir cümle, ikinci parça ve üçüncü parça"),
            sentence => Assert.Contains(sentence[^1], ".,!?:"));

    // ── Reading order ─────────────────────────────────────────────────────

    private static SpeechSource Story() => new()
    {
        Title = "Anthropic MCP sunucularını uzaktan açtı",
        Summary = "Sunucular artık uzak makinede barındırılabiliyor.",
        WhyItMatters = "Kurulum yükü ortadan kalkıyor.",
        KeyPoints = ["OAuth zorunlu", "Yerel destek duruyor"],
    };

    [Fact]
    public void The_headline_is_read_first()
    {
        var chunks = SpeechScript.Build(Story());

        Assert.Equal(SpeechChunkKind.Title, chunks[0].Kind);
    }

    [Fact]
    public void Sections_are_announced_before_they_are_read()
    {
        var chunks = SpeechScript.Build(Story());

        Assert.Contains(chunks, c => c.Kind == SpeechChunkKind.Heading && c.Text == "Neden önemli.");
        Assert.Contains(chunks, c => c.Kind == SpeechChunkKind.Heading && c.Text == "Öne çıkanlar.");
    }

    [Fact]
    public void Each_key_point_is_its_own_utterance() =>
        Assert.Equal(2, SpeechScript.Build(Story()).Count(c => c.Kind == SpeechChunkKind.KeyPoint));

    [Fact]
    public void A_section_that_only_repeats_the_headline_is_dropped()
    {
        var chunks = SpeechScript.Build(Story() with
        {
            Dek = "Anthropic MCP sunucularını uzaktan açtı",
        });

        Assert.DoesNotContain(chunks, c => c.Kind == SpeechChunkKind.Dek);
    }

    [Fact]
    public void A_dek_that_adds_something_is_kept()
    {
        var chunks = SpeechScript.Build(Story() with
        {
            Dek = "Yetkilendirme protokolü de değişti.",
        });

        Assert.Contains(chunks, c => c.Kind == SpeechChunkKind.Dek);
    }

    [Fact]
    public void An_empty_section_produces_no_heading()
    {
        var chunks = SpeechScript.Build(Story() with { WhyItMatters = "   " });

        Assert.DoesNotContain(chunks, c => c.Text == "Neden önemli.");
    }

    [Fact]
    public void A_story_with_only_a_headline_still_produces_a_script()
    {
        var chunks = SpeechScript.Build(new SpeechSource { Title = "Kısa haber" });

        Assert.Single(chunks);
    }

    [Fact]
    public void The_same_story_always_produces_the_same_script()
    {
        var first = SpeechScript.Build(Story());
        var second = SpeechScript.Build(Story());

        Assert.Equal(first, second);
    }

    [Fact]
    public void The_duration_estimate_grows_with_the_script()
    {
        var short_ = SpeechDuration.SecondsFor(SpeechScript.Build(new SpeechSource { Title = "Kısa haber" }));
        var long_ = SpeechDuration.SecondsFor(SpeechScript.Build(Story()));

        Assert.True(long_ > short_);
        Assert.True(short_ > 0);
    }
}
