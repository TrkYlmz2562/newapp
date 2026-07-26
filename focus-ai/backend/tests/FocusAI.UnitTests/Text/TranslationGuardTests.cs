using FocusAI.Domain.Text;
using Xunit;

namespace FocusAI.UnitTests.Text;

public class TranslationGuardTests
{
    // The measured LibreTranslate/Argos en→tr output for these exact sentences.
    // They are the reason this guard exists, so they are the first thing it must
    // catch: every one of them is fluent, confident Turkish that says something
    // the source never said.
    [Theory]
    // "React 20 ships a new compiler" → "Reak 20 gemi" — product name corrupted,
    // "ships" read as boats.
    [InlineData("React 20 ships a new compiler.", "Reak 20 gemi yeni bir derleyici.")]
    // The live public instance dropped the product name entirely.
    [InlineData("React 20 ships a new compiler.", "20 gemi yeni bir derleyici.")]
    // "bug" → "boğa" (a bull). The version number is what catches it here.
    [InlineData("OpenSSH 10.2 fixes a critical bug.", "OpenSSH bir boğa düzeltiyor.")]
    [InlineData("The Kubernetes 1.34 release removes in-tree drivers.",
        "Kubernetes salıver ağaç sürücüleri kaldırıyor.")]
    public void Translations_that_drop_an_identifier_are_refused(string source, string candidate)
    {
        var verdict = TranslationGuard.Inspect(source, candidate, out var accepted);

        Assert.Equal(TranslationRejection.LostIdentifier, verdict);

        // Refusal must hand back the original: publishing English is the safe
        // direction, publishing "boğa" is not.
        Assert.Equal(source, accepted);
    }

    [Fact]
    public void A_faithful_translation_is_accepted()
    {
        const string source = "OpenSSH 10.2 fixes a critical bug in the server.";
        const string candidate = "OpenSSH 10.2, sunucudaki kritik bir hatayı düzeltiyor.";

        Assert.Equal(TranslationRejection.None, TranslationGuard.Inspect(source, candidate, out var accepted));
        Assert.Equal(candidate, accepted);
    }

    [Fact]
    public void Turkish_suffixes_on_an_identifier_still_count_as_preserved()
    {
        // "OpenSSH'in" and "10.2'de" contain the identifier, which is why the check
        // is containment rather than token equality.
        const string source = "OpenSSH 10.2 changes the default cipher.";
        const string candidate = "OpenSSH'in 10.2'deki varsayılan şifrelemesi değişti.";

        Assert.Equal(TranslationRejection.None, TranslationGuard.Inspect(source, candidate, out _));
    }

    [Theory]
    [InlineData("CVE-2026-1234")]
    [InlineData("v1.90.2")]
    [InlineData("GPT-5")]
    [InlineData("PostgreSQL")]
    [InlineData("2026")]
    [InlineData("GPU")]
    [InlineData("Kubernetes")]
    public void Losing_a_protected_token_is_refused(string token)
    {
        // Asserted through the decision rather than the token list: what matters is
        // that a translation dropping the token cannot be published.
        var source = $"The report mentions {token} in some detail.";

        Assert.Equal(
            TranslationRejection.LostIdentifier,
            TranslationGuard.Inspect(source, "Rapor bunu biraz ayrıntılı biçimde ele alıyor.", out _));

        Assert.Equal(
            TranslationRejection.None,
            TranslationGuard.Inspect(source, $"Rapor, {token} konusunu biraz ayrıntılı ele alıyor.", out _));
    }

    [Fact]
    public void A_product_name_opening_the_sentence_is_still_protected()
    {
        // The lede of a tech story opens with its subject, so this is where the
        // most important token in the segment lives.
        const string source = "Kubernetes removes the in-tree cloud provider drivers.";

        Assert.Equal(
            TranslationRejection.LostIdentifier,
            TranslationGuard.Inspect(source, "Bulut sağlayıcı sürücüleri kaldırılıyor.", out _));
    }

    [Fact]
    public void An_ordinary_word_opening_the_sentence_is_not_protected()
    {
        // "Developers" must be free to become "Geliştiriciler" — requiring it to
        // survive verbatim would reject every correct translation of this sentence.
        const string source = "Developers can now pin the compiler to a stable channel.";

        Assert.Equal(
            TranslationRejection.None,
            TranslationGuard.Inspect(source, "Geliştiriciler artık derleyiciyi kararlı kanala sabitleyebilir.", out _));
    }

    [Fact]
    public void Ordinary_hyphenated_english_is_not_frozen_in_place()
    {
        // "in-tree" has no digit, so it is not treated as a code token and may be
        // translated like any other phrase.
        const string source = "The change removes the in-tree drivers from the project.";

        Assert.Equal(
            TranslationRejection.None,
            TranslationGuard.Inspect(source, "Değişiklik, ağaç içi sürücüleri projeden kaldırıyor.", out _));
    }

    [Fact]
    public void An_all_caps_headline_does_not_make_every_word_an_identifier()
    {
        // Every word scores as an acronym under a naive rule, which would reject
        // every possible translation of an all-caps headline. Same trap
        // VisualSubject hits when scoring headlines.
        const string source = "OPENSSH SUNUCULARI KRITIK BIR ACIK ICERIYOR";

        var identifiers = TranslationGuard.Identifiers(source);

        Assert.DoesNotContain("KRITIK", identifiers);
        Assert.DoesNotContain("SUNUCULARI", identifiers);
    }

    [Fact]
    public void Runaway_repetition_is_refused()
    {
        // The documented collapse mode of these models: the decoder loops instead
        // of stopping. Length is the only signal available, and it is enough.
        const string source = "The new runtime improves compile times by 15 percent on large projects.";
        var candidate = string.Join(' ', Enumerable.Repeat("15 yüzde daha hızlı derleme", 40));

        Assert.Equal(TranslationRejection.LengthRatio, TranslationGuard.Inspect(source, candidate, out _));
    }

    [Fact]
    public void A_collapsed_translation_is_refused()
    {
        const string source =
            "The Kubernetes 1.34 release removes the in-tree cloud provider drivers " +
            "and completes a migration that started four years ago.";

        Assert.Equal(
            TranslationRejection.LengthRatio,
            TranslationGuard.Inspect(source, "Kubernetes 1.34.", out _));
    }

    [Fact]
    public void Answering_in_the_wrong_language_is_refused()
    {
        // A multilingual model takes its target from the prompt, so returning
        // Russian or Chinese is a real failure mode rather than a hypothetical.
        const string source = "The compiler now runs twice as fast on large projects.";

        Assert.Equal(
            TranslationRejection.WrongScript,
            TranslationGuard.Inspect(source, "Компилятор теперь работает вдвое быстрее.", out _));
    }

    [Fact]
    public void Turkish_letters_are_not_mistaken_for_a_foreign_script()
    {
        const string source = "The update changes how the scheduler assigns work to idle nodes.";
        const string candidate = "Güncelleme, zamanlayıcının boştaki düğümlere işi nasıl dağıttığını değiştiriyor.";

        Assert.Equal(TranslationRejection.None, TranslationGuard.Inspect(source, candidate, out _));
    }

    [Fact]
    public void A_leading_preamble_is_stripped_rather_than_rejected()
    {
        const string source = "The release notes list twelve fixes.";

        var verdict = TranslationGuard.Inspect(
            source,
            "Here is the translation: Sürüm notları on iki düzeltme sıralıyor.",
            out var accepted);

        // The translation itself is fine; the preamble is a formatting slip and
        // throwing the whole thing away would cost a good translation.
        Assert.Equal(TranslationRejection.None, verdict);
        Assert.Equal("Sürüm notları on iki düzeltme sıralıyor.", accepted);
    }

    [Fact]
    public void A_reply_that_is_only_a_preamble_is_refused()
    {
        const string source = "The release notes list twelve fixes.";

        Assert.Equal(
            TranslationRejection.Preamble,
            TranslationGuard.Inspect(source, "Sure, here is the translation:", out var accepted));

        Assert.Equal(source, accepted);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_reply_is_refused(string? candidate)
    {
        const string source = "The release notes list twelve fixes.";

        Assert.Equal(TranslationRejection.Empty, TranslationGuard.Inspect(source, candidate, out var accepted));
        Assert.Equal(source, accepted);
    }

    [Fact]
    public void A_short_segment_is_not_judged_on_length()
    {
        // "GPT-5" is a legitimate whole segment, and a 3x swing on five characters
        // says nothing about quality.
        Assert.Equal(TranslationRejection.None, TranslationGuard.Inspect("Rust 1.90", "Rust 1.90 sürümü", out _));
    }

    [Theory]
    [InlineData("tr")]
    [InlineData("TR")]
    [InlineData("tr-TR")]
    [InlineData(" tr ")]
    [InlineData("tur")]
    public void Turkish_sources_are_not_translated(string language) =>
        Assert.False(TranslationGuard.NeedsTurkish(language));

    [Theory]
    [InlineData("en")]
    [InlineData("en-US")]
    [InlineData("de")]
    public void Foreign_sources_are_translated(string language) =>
        Assert.True(TranslationGuard.NeedsTurkish(language));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void An_unknown_language_is_treated_as_foreign(string? language)
    {
        // Safe because the guard is what decides the outcome: a needless attempt on
        // Turkish text either round-trips harmlessly or is refused.
        Assert.True(TranslationGuard.NeedsTurkish(language));
    }

    [Theory]
    [InlineData("Merkez Bankası faiz kararını açıkladı ve piyasalar tepki verdi.")]
    [InlineData("Yeni sürüm, geliştiricilerin uzun süredir beklediği değişikliği içeriyor.")]
    [InlineData("Bu değişiklik için hazırlık yapıldı ve sonra devreye alındı.")]
    public void Text_that_is_already_turkish_is_recognised(string text) =>
        Assert.True(TranslationGuard.LooksTurkish(text));

    [Theory]
    [InlineData("The central bank announced its rate decision and markets reacted.")]
    [InlineData("OpenSSH 10.2 fixes a critical bug in the server component.")]
    // German shares ö/ü/ç with Turkish, which is why those letters are not the
    // signal — only ğ, ş and dotted/dotless i are.
    [InlineData("Die Bundesregierung hat über die Förderung für größere Projekte entschieden.")]
    [InlineData("")]
    public void Text_that_is_not_turkish_is_not_mistaken_for_it(string text) =>
        Assert.False(TranslationGuard.LooksTurkish(text));

    [Theory]
    // A "starts with a capital" rule misses every one of these.
    [InlineData("iOS")]
    [InlineData("gRPC")]
    [InlineData("eBPF")]
    [InlineData("macOS")]
    [InlineData("Go")]
    [InlineData("Qt")]
    public void Lowercase_initial_and_two_letter_product_names_are_protected(string name)
    {
        var source = $"The team rewrote the agent in {name} for the next release.";

        Assert.Equal(
            TranslationRejection.LostIdentifier,
            TranslationGuard.Inspect(source, "Ekip, ajanı bir sonraki sürüm için yeniden yazdı.", out _));
    }

    [Fact]
    public void An_acronym_dense_mixed_case_title_still_protects_its_acronyms()
    {
        // A "mostly uppercase" test calls this shouting and then disables both the
        // acronym and proper-noun rules — leaving the densest segment unprotected.
        const string source = "GPU ve TPU Fiyatları SSD ile Birlikte Düştü";

        Assert.Equal(
            TranslationRejection.LostIdentifier,
            TranslationGuard.Inspect(source, "Fiyatlar birlikte düştü", out _));
    }

    [Theory]
    [InlineData("Istanbul", "İstanbul")]
    [InlineData("Izmir", "İzmir")]
    public void Turkish_dotted_capital_i_counts_as_the_same_identifier(string english, string turkish)
    {
        // OrdinalIgnoreCase does not fold İ to I, so without explicit folding the
        // correct Turkish spelling reads as a lost identifier.
        var source = $"The new data centre opened in {english} last month.";

        Assert.Equal(
            TranslationRejection.None,
            TranslationGuard.Inspect(source, $"Yeni veri merkezi geçen ay {turkish}'da açıldı.", out _));
    }

    [Theory]
    [InlineData("US", "ABD")]
    [InlineData("EU", "AB")]
    public void Acronyms_that_turkish_localises_are_not_required_to_survive(string english, string turkish)
    {
        var source = $"The {english} regulator approved the merger this week.";

        Assert.Equal(
            TranslationRejection.None,
            TranslationGuard.Inspect(source, $"{turkish} düzenleyicisi birleşmeyi bu hafta onayladı.", out _));
    }

    [Fact]
    public void A_decimal_may_be_written_the_turkish_way()
    {
        // Turkish writes 1.5 as 1,5. Requiring the exact token would refuse a
        // correct translation of any sentence carrying a figure.
        const string source = "The upgrade cut the build time by 1.5 seconds on average.";

        Assert.Equal(
            TranslationRejection.None,
            TranslationGuard.Inspect(source, "Güncelleme derleme süresini ortalama 1,5 saniye kısalttı.", out _));
    }

    [Fact]
    public void A_version_number_still_has_to_survive_a_separator_change()
    {
        const string source = "Kubernetes 1.34 removes the in-tree drivers.";

        Assert.Equal(
            TranslationRejection.None,
            TranslationGuard.Inspect(source, "Kubernetes 1,34 ağaç içi sürücüleri kaldırıyor.", out _));

        Assert.Equal(
            TranslationRejection.LostIdentifier,
            TranslationGuard.Inspect(source, "Kubernetes ağaç içi sürücüleri kaldırıyor.", out _));
    }

    [Theory]
    [InlineData("İşte çeviri:")]
    [InlineData("işte çeviri: Sürüm notları on iki düzeltme sıralıyor.")]
    public void The_turkish_preamble_is_actually_matched(string candidate)
    {
        // Writing "İşte" as a literal can put "i" + U+0307 into the pattern, which
        // matches neither spelling — the alternative becomes dead code and the
        // preamble ships as part of the headline.
        const string source = "The release notes list twelve fixes.";

        var verdict = TranslationGuard.Inspect(source, candidate, out var accepted);

        Assert.True(
            verdict == TranslationRejection.Preamble || accepted == "Sürüm notları on iki düzeltme sıralıyor.",
            $"preamble was neither stripped nor rejected; got {verdict} / '{accepted}'");
    }

    [Fact]
    public void English_prose_mentioning_a_turkish_name_is_not_treated_as_turkish()
    {
        // Counting the letters rather than their rate marks this as already-Turkish,
        // and the translator then skips it and publishes the English.
        const string text =
            "The regulator, chaired by Kılıçdaroğlu, said the merger would be reviewed " +
            "again in the autumn once the outstanding filings have been submitted.";

        Assert.False(TranslationGuard.LooksTurkish(text));
    }

    [Fact]
    public void Chunking_splits_on_sentence_boundaries_and_loses_nothing()
    {
        var text = string.Join(' ', Enumerable.Range(1, 12)
            .Select(i => $"Bu {i}. cümledir ve makul bir uzunluğa sahiptir."));

        var chunks = TranslationGuard.Chunk(text, 120);

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, chunk => Assert.EndsWith(".", chunk));

        // Every sentence survives somewhere: chunking must not be a lossy step.
        for (var i = 1; i <= 12; i++)
        {
            Assert.Contains(chunks, chunk => chunk.Contains($"Bu {i}. cümledir"));
        }
    }

    [Fact]
    public void A_sentence_longer_than_the_budget_is_kept_whole()
    {
        // Cutting mid-sentence produces a fragment that cannot be translated into
        // anything meaningful, so the budget yields rather than the sentence.
        var sentence = string.Join(' ', Enumerable.Repeat("kelime", 60)) + ".";

        var chunks = TranslationGuard.Chunk(sentence, 100);

        Assert.Single(chunks);
        Assert.Equal(sentence, chunks[0]);
    }

    [Fact]
    public void Short_text_is_a_single_chunk()
    {
        Assert.Equal(["Kısa bir cümle."], TranslationGuard.Chunk("Kısa bir cümle.", 700));
    }

    [Fact]
    public void Empty_text_yields_no_chunks() => Assert.Empty(TranslationGuard.Chunk("   ", 700));
}
