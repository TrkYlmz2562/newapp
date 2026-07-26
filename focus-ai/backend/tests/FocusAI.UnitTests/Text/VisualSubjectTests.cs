using FocusAI.Domain.Text;
using Xunit;

namespace FocusAI.UnitTests.Text;

/// <summary>
/// The card sets this term in display type, so a wrong pick is highly visible.
/// Cases marked "regression" were produced by running real headline shapes through
/// the extractor; each one shipped a wrong subject before.
/// </summary>
public class VisualSubjectTests
{
    [Theory]
    [InlineData("Apple M5 çip mimarisini tanıttı: yapay zekâ çekirdekleri öne çıkıyor", "M5")]
    [InlineData("OpenAI yeni muhakeme modeli o5'i duyurdu", "o5")]
    [InlineData("Kritik OpenSSH açığı sunucuları tehdit ediyor; acil yama çağrısı yapıldı", "OpenSSH")]
    [InlineData("PostgreSQL 18 yayımlandı", "PostgreSQL 18")]
    [InlineData("React 20 yayımlandı: derleyici varsayılan olarak açık geliyor", "React 20")]
    public void Picks_the_designation_the_headline_is_about(string title, string expected) =>
        Assert.Equal(expected, VisualSubject.FromTitle(title));

    [Fact]
    public void Prefers_a_cve_identifier_over_anything_else() =>
        Assert.Equal(
            "CVE-2026-31337",
            VisualSubject.FromTitle("Kritik OpenSSH açığı CVE-2026-31337 sunucuları tehdit ediyor"));

    [Theory]
    [InlineData("Türk yapay zekâ girişimi 20 milyon dolar yatırım aldı", "20M$")]
    [InlineData("Girişim 1,5 milyar dolar değerleme ile turu kapattı", "1.5B$")]
    [InlineData("Avrupalı girişim 12 milyon euro topladı", "12M€")]
    [InlineData("Girişim 20 milyon TL yatırım aldı", "20M₺")]
    public void Compacts_a_funding_amount_into_a_numeral(string title, string expected) =>
        Assert.Equal(expected, VisualSubject.FromTitle(title));

    [Fact]
    public void Finds_the_amount_even_when_a_count_comes_first() =>
        // regression: Money() used to bail on the first match, so the leading
        // count ("30 milyon", no currency) suppressed the real figure.
        Assert.Equal(
            "20M$",
            VisualSubject.FromTitle("Girişim 30 milyon kullanıcıya ulaştı ve 20 milyon dolar yatırım aldı"));

    [Fact]
    public void Reads_an_amount_in_a_shouted_headline() =>
        // regression: RegexOptions.IgnoreCase never matched "MİLYON" to "milyon".
        Assert.Equal("20M$", VisualSubject.FromTitle("GİRİŞİM 20 MİLYON DOLAR YATIRIM ALDI"));

    [Theory]
    [InlineData("Uygulama 20 milyon kullanıcıya ulaştı")]
    [InlineData("Zoom 5 milyon kullanıcı kaybetti")]
    public void A_count_is_never_dressed_up_as_a_version(string title)
    {
        // regression: these rendered "Uygulama 20" and "Zoom 5".
        var subject = VisualSubject.FromTitle(title);

        Assert.DoesNotContain("20", subject ?? string.Empty);
        Assert.DoesNotContain("5", subject ?? string.Empty);
    }

    [Theory]
    [InlineData("Bakanlık 2026 bütçesini açıkladı")]
    [InlineData("Ocak 2026 enflasyon verisi yayımlandı")]
    public void A_year_is_not_a_version(string title) =>
        // regression: emitted "Bakanlık 2026" / "Ocak 2026".
        Assert.DoesNotContain("2026", VisualSubject.FromTitle(title) ?? string.Empty);

    [Fact]
    public void A_shouted_headline_still_finds_a_designation() =>
        Assert.Equal("M5", VisualSubject.FromTitle("APPLE M5 ÇİPİNİ TANITTI"));

    [Fact]
    public void A_shouted_headline_with_no_designation_yields_nothing() =>
        // regression: returned "GİRİŞİMİ" — the uppercase form slipped the
        // stopword filter, and the longest-wins tie-break picked it.
        Assert.Null(VisualSubject.FromTitle("TÜRKİYE'NİN YENİ YAPAY ZEKÂ GİRİŞİMİ YATIRIM ALDI"));

    [Theory]
    [InlineData("Netflix abonelik ücretlerine zam yaptı", "Netflix")]
    [InlineData("Kubernetes için yeni güvenlik yaması yayımlandı", "Kubernetes")]
    [InlineData("Google is shutting down its cloud gaming service", "Google")]
    [InlineData("Nvidia beats earnings expectations again", "Nvidia")]
    public void A_headline_that_opens_with_its_subject_keeps_it(string title, string expected) =>
        // regression: the index-0 guard zeroed these, so the card fell back to
        // the category label — the generic plate this feature exists to remove.
        Assert.Equal(expected, VisualSubject.FromTitle(title));

    [Theory]
    [InlineData("Microsoft Releases Security Update For Windows", "Microsoft")]
    [InlineData("Google Announces New Cloud Region In Turkey", "Google")]
    public void Title_case_headlines_do_not_return_the_verb(string title, string expected) =>
        // regression: returned "Releases" / "Announces" — in Title Case the
        // capitalisation carries no signal and length decided the winner.
        Assert.Equal(expected, VisualSubject.FromTitle(title));

    [Fact]
    public void The_category_word_is_never_the_subject() =>
        // regression: "AI" scored as a product via its internal capital, and
        // rejecting the model's identical answer just re-derived it.
        Assert.NotEqual("AI", VisualSubject.FromTitle("Microsoft brings AI to Windows"));

    [Fact]
    public void Skips_capitalised_filler_words() =>
        Assert.Equal(
            "Kubernetes",
            VisualSubject.FromTitle("Yeni Kritik güncelleme Kubernetes için yayımlandı"));

    [Fact]
    public void Sentence_case_filler_alone_yields_nothing() =>
        Assert.Null(VisualSubject.FromTitle("Geliştiriciler için yeni bir yol haritası açıklandı"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_input_yields_nothing(string? title) =>
        Assert.Null(VisualSubject.FromTitle(title));

    [Fact]
    public void Rejects_rather_than_truncates_an_over_long_subject()
    {
        var subject = VisualSubject.FromTitle("Şirket SupercalifragilisticExpialidocious9 duyurdu", maxLength: 12);

        Assert.True(subject is null || subject.Length <= 12);
    }

    // ---- Sanitize: the gate on model output ----

    [Fact]
    public void Sanitize_keeps_a_model_answer_that_is_in_the_story() =>
        Assert.Equal("o5", VisualSubject.Sanitize("o5", "OpenAI o5 muhakeme modelini duyurdu"));

    [Fact]
    public void Sanitize_accepts_a_term_found_only_in_the_body() =>
        Assert.Equal(
            "Sonnet 5",
            VisualSubject.Sanitize("Sonnet 5", "Anthropic yeni modelini duyurdu", "Şirket Sonnet 5 modelini tanıttı."));

    [Fact]
    public void Sanitize_matches_across_a_turkish_suffix() =>
        // "OpenSSH'in" in the story still counts as containing "OpenSSH".
        Assert.Equal("OpenSSH", VisualSubject.Sanitize("OpenSSH", "OpenSSH'in yeni sürümü yayımlandı"));

    [Theory]
    [InlineData("M5 Pro", "Apple M5 çipini tanıttı", "M5")]
    [InlineData("GPT-6 Turbo", "OpenAI o5 muhakeme modelini duyurdu", "o5")]
    [InlineData("OpenAI", "Anthropic Claude 5 modelini duyurdu", "Claude 5")]
    public void Sanitize_refuses_a_term_that_is_not_in_the_story(string invented, string title, string expected)
    {
        // The whole point of the gate: a hallucinated name must never reach the
        // card, where it would be the largest string on the page.
        Assert.Equal(expected, VisualSubject.Sanitize(invented, title));
    }

    [Theory]
    [InlineData("\"M5\"")]
    [InlineData("“M5”")]
    [InlineData("**M5**")]
    public void Sanitize_strips_decoration_the_model_added(string candidate) =>
        Assert.Equal("M5", VisualSubject.Sanitize(candidate, "Apple M5 çipini tanıttı"));

    [Theory]
    [InlineData("yapay zekâ")]
    [InlineData("Teknoloji")]
    [InlineData("güncelleme")]
    [InlineData("null")]
    [InlineData("Bilinmiyor")]
    [InlineData("")]
    [InlineData(null)]
    public void Sanitize_rejects_a_restatement_of_the_category(string? candidate) =>
        Assert.Equal(
            "OpenSSH",
            VisualSubject.Sanitize(candidate, "Kritik OpenSSH açığı sunucuları tehdit ediyor"));

    [Fact]
    public void Sanitize_never_exceeds_the_column_width()
    {
        var subject = VisualSubject.Sanitize(new string('x', 200), new string('y', 300));

        Assert.True(subject is null || subject.Length <= 24);
    }
}
