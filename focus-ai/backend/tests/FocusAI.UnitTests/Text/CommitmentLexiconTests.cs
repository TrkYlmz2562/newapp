using FocusAI.Domain.Enums;
using FocusAI.Domain.Text;
using Xunit;

namespace FocusAI.UnitTests.Text;

/// <summary>
/// The finance gate rests on these rules rather than on the model's judgement, so
/// a regression here silently promotes speculation into a feed the reader is told
/// contains only committed developments.
/// </summary>
public class CommitmentLexiconTests
{
    // ---- the -mış evidential: two characters that invert the claim ----

    [Fact]
    public void First_hand_past_tense_is_not_hearsay() =>
        Assert.Equal(
            CommitmentTier.Realized,
            CommitmentLexicon.Detect("Şirketler birleşme sözleşmesini imzaladı."));

    [Theory]
    [InlineData("Şirketler birleşme sözleşmesini imzalanmış.")]
    [InlineData("Karar alınmış, taraflar görüşülmüş.")]
    [InlineData("Yeni düzenleme için başvuru yapılmış.")]
    public void The_reported_speech_suffix_caps_at_unverified(string text) =>
        // -mış/-miş/-muş/-müş marks second-hand knowledge. An English-trained model
        // reads it as a perfect tense and treats it as *more* certain.
        Assert.Equal(CommitmentTier.UnverifiedClaim, CommitmentLexicon.Detect(text));

    [Fact]
    public void Formal_officialese_is_not_hearsay() =>
        // "-mıştır" is assertive, not evidential — it is how official texts state facts.
        Assert.Equal(
            CommitmentTier.Realized,
            CommitmentLexicon.Detect("Karar Resmî Gazete'de yayımlanmıştır."));

    // ---- attribution ----

    [Theory]
    [InlineData("Kaynaklara göre bankanın satışı için görüşmeler sürüyor.")]
    [InlineData("İddia edildiğine göre ürün kapatılacak.")]
    [InlineData("Kulislerde yeni vergi düzenlemesi konuşuluyor.")]
    [InlineData("Konuya yakın kaynaklar anlaşmanın imzalandığını söyledi.")]
    public void Anonymous_attribution_caps_at_unverified(string text) =>
        Assert.True(CommitmentLexicon.Detect(text) >= CommitmentTier.UnverifiedClaim);

    [Theory]
    [InlineData("Aracı kurum hedef fiyatı 120 TL'ye yükseltti.")]
    [InlineData("Ekonomistlerin yıl sonu enflasyon medyan beklentisi %25.")]
    [InlineData("Piyasa faiz indirimini fiyatlıyor.")]
    public void Analyst_expectations_are_the_least_certain_class(string text) =>
        Assert.Equal(CommitmentTier.AnalystSpeculation, CommitmentLexicon.Detect(text));

    [Theory]
    [InlineData("Şirket 2030'a kadar 10 milyar dolar yatırım hedefliyor.")]
    [InlineData("Bakanlık yeni teşvik paketi üzerinde çalışıyor.")]
    [InlineData("Faiz indirimi söz konusu olabilir.")]
    public void Intent_and_hedging_cap_at_stated_intent(string text) =>
        Assert.Equal(CommitmentTier.StatedIntent, CommitmentLexicon.Detect(text));

    [Theory]
    [InlineData("İki banka birleşme sözleşmesi imzaladı; işlem Rekabet Kurumu onayına tabi.")]
    [InlineData("Bedelli sermaye artırımı için SPK onayı bekleniyor mu, başvuru yapıldı; SPK onayı gerekiyor.")]
    public void A_named_approval_routes_to_conditional(string text) =>
        Assert.True(CommitmentLexicon.Detect(text) >= CommitmentTier.ConditionalPending);

    [Fact]
    public void The_worst_marker_wins_not_the_average()
    {
        // Heavy official detail plus one hearsay marker is still hearsay: blending is
        // exactly what lets well-sourced speculation look certain.
        var text = "Resmî Gazete'de yayımlanan karara göre oran değişecek; " +
                   "kaynaklara göre uygulama ertelenebilir.";

        Assert.True(CommitmentLexicon.Detect(text) >= CommitmentTier.UnverifiedClaim);
    }

    [Fact]
    public void Uppercase_turkish_markers_are_still_detected() =>
        // OrdinalIgnoreCase does not fold İ, so an uppercase headline used to slip
        // every marker set.
        Assert.True(CommitmentLexicon.Detect("KAYNAKLARA GÖRE SATIŞ GÖRÜŞMELERİ SÜRÜYOR") >= CommitmentTier.UnverifiedClaim);

    [Fact]
    public void Empty_text_is_treated_as_unverified() =>
        Assert.Equal(CommitmentTier.UnverifiedClaim, CommitmentLexicon.Detect("   "));

    // ---- reversals ----

    [Theory]
    [InlineData("Düzenleme iptal edildi.")]
    [InlineData("Toplantı ertelendi.")]
    [InlineData("Teklif geri çekildi.")]
    public void Reversals_are_detected(string text) => Assert.True(CommitmentLexicon.IsReversal(text));

    [Fact]
    public void A_normal_item_is_not_a_reversal() =>
        Assert.False(CommitmentLexicon.IsReversal("Karar 1 Ocak'ta yürürlüğe girecek."));

    // ---- dates ----

    [Theory]
    [InlineData("yakında")]
    [InlineData("kısa süre içinde")]
    [InlineData("önümüzdeki dönem")]
    [InlineData(null)]
    [InlineData("")]
    public void Vague_futures_are_not_dates(string? text) => Assert.True(CommitmentLexicon.IsVagueDate(text));

    [Theory]
    [InlineData("1 Ocak 2027")]
    [InlineData("15 Ekim")]
    [InlineData("3. çeyrek")]
    public void Real_date_expressions_are_dates(string text) => Assert.False(CommitmentLexicon.IsVagueDate(text));

    // ---- horizon ----

    [Theory]
    [InlineData(-1, EventHorizon.Completed)]
    [InlineData(0, EventHorizon.Imminent)]
    [InlineData(7, EventHorizon.Imminent)]
    [InlineData(8, EventHorizon.Near)]
    [InlineData(90, EventHorizon.Near)]
    [InlineData(91, EventHorizon.Mid)]
    [InlineData(365, EventHorizon.Mid)]
    [InlineData(366, EventHorizon.Long)]
    public void Horizon_buckets_are_pure_arithmetic(int daysAhead, EventHorizon expected)
    {
        var now = new DateTimeOffset(2026, 7, 26, 12, 0, 0, TimeSpan.Zero);
        var date = DateOnly.FromDateTime(now.UtcDateTime).AddDays(daysAhead);

        Assert.Equal(expected, CommitmentLexicon.Horizon(date, now));
    }

    [Fact]
    public void No_date_means_undated() =>
        Assert.Equal(EventHorizon.Undated, CommitmentLexicon.Horizon(null, DateTimeOffset.UtcNow));

    // ---- the inclusion matrix ----

    [Fact]
    public void An_undated_item_is_never_publishable()
    {
        foreach (var tier in Enum.GetValues<CommitmentTier>())
        {
            Assert.False(CommitmentLexicon.IsPublishable(tier, EventHorizon.Undated, FinanceInstrument.ResmiGazete));
        }
    }

    [Fact]
    public void A_dated_official_promise_is_published_when_it_is_near_but_not_when_it_is_far()
    {
        // The bar rises with the horizon — this is the rule that keeps the feed from
        // filling up with political pledges about 2030.
        Assert.True(CommitmentLexicon.IsPublishable(
            CommitmentTier.OfficialCommitment, EventHorizon.Near, FinanceInstrument.KurumKarari));

        Assert.False(CommitmentLexicon.IsPublishable(
            CommitmentTier.OfficialCommitment, EventHorizon.Long, FinanceInstrument.KurumKarari));
    }

    [Fact]
    public void A_published_institutional_calendar_survives_the_mid_horizon() =>
        // TCMB's rate-decision calendar is committed a year ahead in a document;
        // that is a different thing from a minister's promise.
        Assert.True(CommitmentLexicon.IsPublishable(
            CommitmentTier.OfficialCommitment, EventHorizon.Mid, FinanceInstrument.ResmiTakvim));

    [Fact]
    public void A_binding_instrument_survives_any_horizon() =>
        Assert.True(CommitmentLexicon.IsPublishable(
            CommitmentTier.EnactedDated, EventHorizon.Long, FinanceInstrument.ResmiGazete));

    [Theory]
    [InlineData(CommitmentTier.ConditionalPending)]
    [InlineData(CommitmentTier.StatedIntent)]
    [InlineData(CommitmentTier.UnverifiedClaim)]
    [InlineData(CommitmentTier.AnalystSpeculation)]
    [InlineData(CommitmentTier.Unknown)]
    public void Nothing_below_official_commitment_ever_reaches_the_feed(CommitmentTier tier)
    {
        foreach (var horizon in Enum.GetValues<EventHorizon>())
        {
            Assert.False(CommitmentLexicon.IsPublishable(tier, horizon, FinanceInstrument.ResmiGazete));
        }
    }

    [Fact]
    public void Only_a_realized_event_appears_under_the_completed_horizon()
    {
        Assert.True(CommitmentLexicon.IsPublishable(
            CommitmentTier.Realized, EventHorizon.Completed, FinanceInstrument.Kap));

        Assert.False(CommitmentLexicon.IsPublishable(
            CommitmentTier.EnactedDated, EventHorizon.Completed, FinanceInstrument.Kap));
    }
}
