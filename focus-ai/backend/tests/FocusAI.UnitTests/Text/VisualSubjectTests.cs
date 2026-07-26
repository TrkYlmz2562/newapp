using FocusAI.Domain.Text;
using Xunit;

namespace FocusAI.UnitTests.Text;

/// <summary>
/// The card sets this term in display type, so a wrong pick is highly visible.
/// These cases are the real headline shapes the feed produces.
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
    public void Compacts_a_funding_amount_into_a_numeral(string title, string expected) =>
        Assert.Equal(expected, VisualSubject.FromTitle(title));

    [Fact]
    public void A_count_is_not_an_amount()
    {
        // "20 milyon kullanıcı" must never render as "20M$" — there is no currency.
        var subject = VisualSubject.FromTitle("Uygulama 20 milyon kullanıcıya ulaştı");

        Assert.NotEqual("20M$", subject);
    }

    [Fact]
    public void Skips_capitalised_filler_words()
    {
        // "Yeni" and "Kritik" are capitalised but carry no identity.
        var subject = VisualSubject.FromTitle("Yeni Kritik güncelleme Kubernetes için yayımlandı");

        Assert.Equal("Kubernetes", subject);
    }

    [Fact]
    public void Sentence_case_alone_does_not_make_a_subject()
    {
        // The first word is capitalised by grammar, not because it is a product.
        Assert.Null(VisualSubject.FromTitle("Geliştiriciler için yeni bir yol haritası açıklandı"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_input_yields_nothing(string? title) =>
        Assert.Null(VisualSubject.FromTitle(title));

    [Fact]
    public void Rejects_rather_than_truncates_an_over_long_subject()
    {
        // A clipped subject set at 74px reads as broken; the caller has a fallback.
        var subject = VisualSubject.FromTitle("Şirket SupercalifragilisticExpialidocious9 duyurdu", maxLength: 12);

        Assert.True(subject is null || subject.Length <= 12);
    }

    [Fact]
    public void Sanitize_keeps_a_specific_model_answer() =>
        Assert.Equal("o5", VisualSubject.Sanitize("o5", "OpenAI muhakeme modelini duyurdu"));

    [Fact]
    public void Sanitize_strips_quoting_the_model_added() =>
        Assert.Equal("M5", VisualSubject.Sanitize("\"M5\"", "Apple yeni çipi tanıttı"));

    [Theory]
    [InlineData("yapay zekâ")]
    [InlineData("Teknoloji")]
    [InlineData("güncelleme")]
    [InlineData("null")]
    [InlineData("")]
    [InlineData(null)]
    public void Sanitize_rejects_a_restatement_of_the_category_and_re_derives(string? candidate)
    {
        // These name the category, which the card already shows — so the term is
        // re-derived from the headline instead.
        var subject = VisualSubject.Sanitize(candidate, "Kritik OpenSSH açığı sunucuları tehdit ediyor");

        Assert.Equal("OpenSSH", subject);
    }

    [Fact]
    public void Sanitize_never_exceeds_the_column_width()
    {
        var subject = VisualSubject.Sanitize(new string('x', 200), new string('y', 300));

        Assert.True(subject is null || subject.Length <= 24);
    }
}
