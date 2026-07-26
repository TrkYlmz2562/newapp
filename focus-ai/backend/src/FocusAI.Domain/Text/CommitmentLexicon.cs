using System.Text;
using FocusAI.Domain.Enums;

namespace FocusAI.Domain.Text;

/// <summary>
/// Reads a Turkish finance item's own words and reports the most it can honestly
/// claim. Deterministic, so the publication gate does not rest on a model's
/// judgement.
/// </summary>
/// <remarks>
/// The rule everywhere here is weakest-link, never an average: one hearsay marker
/// outranks any amount of corroborating detail. Blending is exactly what lets a
/// well-sourced piece of speculation look certain, and in a finance feed that
/// error costs the reader money — so every ambiguity resolves downward.
/// </remarks>
public static class CommitmentLexicon
{
    /// <summary>A third party's expectation. Numeric precision here is the trap, not the evidence.</summary>
    private static readonly string[] AnalystMarkers =
    [
        "hedef fiyat", "al tavsiyesi", "tut tavsiyesi", "sat tavsiyesi", "tavsiyesi verdi",
        "notunu yukseltti", "notunu dusurdu", "arastirma raporu", "analist", "stratejist",
        "ekonomistler", "medyan beklenti", "konsensus", "piyasa fiyatliyor", "fiyatliyor",
        "senaryo analizi", "teknik olarak destek", "teknik olarak direnc", "beklentisi",
        "bekleniyor", "tahmin ediliyor", "ongoruluyor", "gorebilir", "yukselebilir",
        "dusebilir", "hedefliyor analistler"
    ];

    /// <summary>Anonymous attribution and hearsay. Hard cap: no override path.</summary>
    private static readonly string[] HearsayMarkers =
    [
        "kaynaklara gore", "kaynaklarin verdigi bilgiye gore", "konuya yakin kaynaklar",
        "isminin aciklanmasini istemeyen", "ust duzey bir yetkili", "edinilen bilgiye gore",
        "ogrenildi", "ogrenilirken", "kulislerde", "iddiaya gore", "iddia edildi",
        "ileri suruldu", "one suruldu", "sizdi", "basina yansiyan", "soylentiye gore",
        "duyumlara gore"
    ];

    /// <summary>Epistemic hedging about the core event.</summary>
    private static readonly string[] HedgeMarkers =
    [
        "olabilir", "olasi", "muhtemel", "ihtimal", "gibi gorunuyor", "sinyal verdi",
        "kapiyi araladi", "masada", "gundemde", "soz konusu olabilir", "planlaniyor",
        "hedefleniyor", "amaclaniyor", "degerlendiriliyor",
        // Active and passive both appear in the wild; listing one silently misses half.
        "uzerinde calisiliyor", "uzerinde calisiyor", "gorusuluyor", "gorusuyor",
        "masaya yatirildi", "niyet mektubu", "hedefliyor", "planliyor", "amacliyor"
    ];

    /// <summary>
    /// A named, checkable gate. Routes to ConditionalPending rather than lower — a
    /// signed deal awaiting a regulator is genuinely different from a rumour.
    /// </summary>
    private static readonly string[] ConditionMarkers =
    [
        "onayina tabi", "onayina sunulacak", "onay alinmasi halinde", "sartiyla", "kaydiyla",
        "gerekli izinlerin alinmasi", "yasal sureclerin tamamlanmasi", "kapanis kosullari",
        "spk onayi", "bddk izni", "bddk onayi", "rekabet kurumu izni", "rekabet kurumu onayi",
        "epdk onayi", "genel kurul onayina", "on izin", "tescil edilmesiyle"
    ];

    /// <summary>
    /// Vague futures that read like dates but resolve to nothing. An event with no
    /// resolvable date cannot be called near-certain — there is no "when".
    /// </summary>
    private static readonly string[] NonDates =
    [
        "yakinda", "kisa sure icinde", "en kisa surede", "onumuzdeki donem",
        "ilerleyen gunlerde", "orta vadede", "ileride", "zamani gelince",
        "surec tamamlandiginda", "yakin zamanda"
    ];

    /// <summary>Reversals. These do not lower the tier — they invalidate the item.</summary>
    private static readonly string[] ReversalMarkers =
    [
        "iptal edildi", "geri cekildi", "ertelendi", "yururlugu durduruldu",
        "iptali icin dava", "askiya alindi", "rafa kaldirildi", "vazgecildi"
    ];

    /// <summary>
    /// The least-certain tier the text's own markers permit. <see cref="CommitmentTier.Realized"/>
    /// means "nothing here restricts it" — it is not a claim that the event happened.
    /// </summary>
    public static CommitmentTier Detect(string? text)
    {
        var folded = Fold(text);
        if (folded.Length == 0)
        {
            return CommitmentTier.UnverifiedClaim;
        }

        var worst = CommitmentTier.Realized;

        if (ContainsAny(folded, AnalystMarkers))
        {
            worst = Worse(worst, CommitmentTier.AnalystSpeculation);
        }

        if (ContainsAny(folded, HearsayMarkers) || HasEvidentialSuffix(folded))
        {
            worst = Worse(worst, CommitmentTier.UnverifiedClaim);
        }

        if (ContainsAny(folded, HedgeMarkers))
        {
            worst = Worse(worst, CommitmentTier.StatedIntent);
        }

        if (ContainsAny(folded, ConditionMarkers))
        {
            worst = Worse(worst, CommitmentTier.ConditionalPending);
        }

        return worst;
    }

    /// <summary>True when the text says the event was cancelled, withdrawn or postponed.</summary>
    public static bool IsReversal(string? text) => ContainsAny(Fold(text), ReversalMarkers);

    /// <summary>True when the phrase only looks like a date — "yakında" is not a date.</summary>
    public static bool IsVagueDate(string? dateText)
    {
        var folded = Fold(dateText);
        return folded.Length == 0 || ContainsAny(folded, NonDates);
    }

    /// <summary>
    /// Turkish marks reported speech grammatically, with the -mış/-miş/-muş/-müş
    /// suffix: "imzalandı" is first-hand, "imzalanmış" is second-hand. The two
    /// differ by two characters and invert the claim, and a model trained mostly on
    /// English reads the second as a perfect tense — i.e. as *more* certain.
    /// </summary>
    /// <remarks>
    /// The -mıştır form is excluded on purpose: it is assertive officialese
    /// ("Resmî Gazete'de yayımlanmıştır"), not hearsay.
    /// </remarks>
    public static bool HasEvidentialSuffix(string foldedText)
    {
        foreach (var word in foldedText.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (word.Length < 6)
            {
                continue;
            }

            if (word.EndsWith("mistir", StringComparison.Ordinal) ||
                word.EndsWith("mustur", StringComparison.Ordinal))
            {
                continue;
            }

            if (word.EndsWith("mis", StringComparison.Ordinal) ||
                word.EndsWith("mus", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Bucket the event date. Pure arithmetic — the model never computes this.</summary>
    public static EventHorizon Horizon(DateOnly? eventDate, DateTimeOffset now)
    {
        if (eventDate is not { } date)
        {
            return EventHorizon.Undated;
        }

        var days = date.DayNumber - DateOnly.FromDateTime(now.UtcDateTime).DayNumber;

        return days switch
        {
            < 0 => EventHorizon.Completed,
            <= 7 => EventHorizon.Imminent,
            <= 90 => EventHorizon.Near,
            <= 365 => EventHorizon.Mid,
            _ => EventHorizon.Long
        };
    }

    /// <summary>
    /// The inclusion matrix. The bar rises with the horizon: commitment is measured
    /// at publication time but the event sits in the future, and the longer the gap
    /// the more room there is for reversal. A minister's dated promise about 2030
    /// never enters the feed; the same promise about next Tuesday does.
    /// </summary>
    public static bool IsPublishable(CommitmentTier tier, EventHorizon horizon, FinanceInstrument instrument) =>
        horizon switch
        {
            EventHorizon.Completed => tier == CommitmentTier.Realized,

            EventHorizon.Imminent or EventHorizon.Near =>
                tier is CommitmentTier.Realized or CommitmentTier.EnactedDated
                    or CommitmentTier.OfficialCommitment,

            // Past a quarter, an announcement is no longer enough on its own — but a
            // published institutional calendar (PPK, TÜİK, Hazine) is, because the
            // institution has committed the date in a document.
            EventHorizon.Mid =>
                tier is CommitmentTier.Realized or CommitmentTier.EnactedDated ||
                (tier == CommitmentTier.OfficialCommitment && instrument == FinanceInstrument.ResmiTakvim),

            EventHorizon.Long =>
                tier is CommitmentTier.Realized or CommitmentTier.EnactedDated,

            _ => false
        };

    /// <summary>True when the item belongs in the "Şarta Bağlı" tab rather than the feed.</summary>
    public static bool IsConditional(CommitmentTier tier, EventHorizon horizon) =>
        tier == CommitmentTier.ConditionalPending && horizon != EventHorizon.Undated;

    /// <summary>Of two tiers, the less committed one.</summary>
    public static CommitmentTier Worse(CommitmentTier a, CommitmentTier b) => (int)a >= (int)b ? a : b;

    private static bool ContainsAny(string foldedText, string[] markers) =>
        markers.Any(marker => foldedText.Contains(marker, StringComparison.Ordinal));

    /// <summary>
    /// Folds Turkish casing and diacritics for comparison. Written out rather than
    /// using ToLowerInvariant because that leaves 'İ' as U+0130, matching neither
    /// 'i' nor 'I' — every marker containing a dotted i would silently never fire.
    /// </summary>
    public static string Fold(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        var lastWasSpace = true;

        foreach (var ch in value)
        {
            var mapped = ch switch
            {
                'İ' or 'I' or 'ı' or 'Î' or 'î' => 'i',
                'Ş' or 'ş' => 's',
                'Ğ' or 'ğ' => 'g',
                'Ç' or 'ç' => 'c',
                'Ö' or 'ö' => 'o',
                'Ü' or 'ü' or 'Û' or 'û' => 'u',
                'Â' or 'â' => 'a',
                _ => char.ToLowerInvariant(ch)
            };

            if (char.IsLetterOrDigit(mapped))
            {
                builder.Append(mapped);
                lastWasSpace = false;
            }
            else if (!lastWasSpace)
            {
                builder.Append(' ');
                lastWasSpace = true;
            }
        }

        return builder.ToString().Trim();
    }
}
