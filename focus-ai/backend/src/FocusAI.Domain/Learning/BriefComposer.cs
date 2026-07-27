using System.Text;
using FocusAI.Domain.Enums;

namespace FocusAI.Domain.Learning;

/// <summary>
/// Renders a <see cref="BriefCaseFile"/> into the text the reader pastes into
/// <see cref="FocusMentorPersona"/>.
/// </summary>
/// <remarks>
/// This is the half of a brief that no model touches. Outlet names, dates, trust
/// scores, verbatim quotes and the reader's own note are copied through verbatim,
/// so the one thing a language model reliably gets wrong — attributing a claim to
/// the wrong source — cannot happen here. The planner only ever adds questions
/// around this block; it never rewrites it.
///
/// Pure and deterministic: same case file in, byte-identical prompt out. That is
/// what makes it testable, and what lets a stored prompt be compared against a
/// freshly composed one when the format changes.
/// </remarks>
public static class BriefComposer
{
    private static readonly string[] Months =
    [
        "Ocak", "Şubat", "Mart", "Nisan", "Mayıs", "Haziran",
        "Temmuz", "Ağustos", "Eylül", "Ekim", "Kasım", "Aralık"
    ];

    /// <summary>Sources beyond this add length without adding evidence.</summary>
    private const int MaxSources = 12;

    private const int MaxComparisons = 6;

    private const int MaxKeyPoints = 6;

    private const int MaxQuoteLength = 400;

    private const int MaxAnchorQuestions = 6;

    private static readonly IReadOnlyDictionary<ComparisonKind, string> ComparisonLabels =
        new Dictionary<ComparisonKind, string>
        {
            [ComparisonKind.Shared] = "ortak",
            [ComparisonKind.Divergent] = "ayrışma",
            [ComparisonKind.Unique] = "tek kaynakta"
        };

    private static readonly IReadOnlyDictionary<CommitmentTier, string> TierLabels =
        new Dictionary<CommitmentTier, string>
        {
            [CommitmentTier.Unknown] = "sınıflandırılmadı",
            [CommitmentTier.Realized] = "gerçekleşti",
            [CommitmentTier.EnactedDated] = "yürürlükte, tarihi belli",
            [CommitmentTier.OfficialCommitment] = "resmî taahhüt",
            [CommitmentTier.ConditionalPending] = "şarta bağlı",
            [CommitmentTier.StatedIntent] = "niyet/hedef, bağlayıcı değil",
            [CommitmentTier.UnverifiedClaim] = "doğrulanmamış iddia",
            [CommitmentTier.AnalystSpeculation] = "analist tahmini"
        };

    public static string Compose(BriefCaseFile file, BriefPlan? plan = null)
    {
        ArgumentNullException.ThrowIfNull(file);

        var text = new StringBuilder();

        text.AppendLine($"# Ders dosyası: {OneLine(file.Title)}");
        text.AppendLine();
        text.AppendLine(
            "Bu dosya Focus AI'dan geliyor. Haberi zaten okudum — özetini bana geri anlatma.");
        text.AppendLine();
        text.AppendLine($"- **Süre:** {file.Minutes} dakika");

        if (!string.IsNullOrWhiteSpace(plan?.LearningGoal))
        {
            text.AppendLine($"- **Hedef:** {OneLine(plan.LearningGoal)}");
        }

        if (plan?.EntryLevel is { } level)
        {
            var reason = string.IsNullOrWhiteSpace(plan.EntryReason)
                ? string.Empty
                : $" — {OneLine(plan.EntryReason)}";

            text.AppendLine($"- **Önerilen giriş:** {FocusMentorPersona.LevelLabel(level)}{reason}");
        }

        if (!string.IsNullOrWhiteSpace(file.DailyRationale))
        {
            text.AppendLine($"- **Neden bugün:** {OneLine(file.DailyRationale)}");
        }

        foreach (var story in file.Stories)
        {
            text.AppendLine();
            text.AppendLine("---");
            text.AppendLine();
            AppendStory(text, story);
        }

        if (file.Stories.Count == 0)
        {
            text.AppendLine();
            text.AppendLine("*(Bu ders için bağlı bir haber yok.)*");
        }

        AppendPlan(text, plan);
        AppendClosing(text, plan);

        return text.ToString().TrimEnd() + "\n";
    }

    private static void AppendStory(StringBuilder text, BriefStoryFile story)
    {
        text.AppendLine($"## {OneLine(story.Title)}");
        text.AppendLine();
        text.AppendLine($"*{FormatDate(story.PublishedAt)}*");
        text.AppendLine();

        if (!string.IsNullOrWhiteSpace(story.Dek))
        {
            text.AppendLine(OneLine(story.Dek));
            text.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(story.Summary))
        {
            text.AppendLine(OneLine(story.Summary));
            text.AppendLine();
        }

        text.AppendLine(EvidenceRegister(story));
        text.AppendLine();

        if (story.HasEvidentialClaim)
        {
            // The one hearsay signal that works with no model in the loop, so it is
            // also the one the mentor must never quietly upgrade to fact.
            text.AppendLine(
                "> ⚠ Bu haberin metni iddiayı **aktarıyor**, doğrulamıyor (-mış kipi: " +
                "\"iddia edildi\", \"öğrenildi\"). Ders boyunca içeriğini teyit edilmemiş " +
                "iddia olarak ele al.");
            text.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(story.WhyItMatters))
        {
            text.AppendLine($"**Neden önemli:** {OneLine(story.WhyItMatters)}");
        }

        if (!string.IsNullOrWhiteSpace(story.WhoIsAffected))
        {
            text.AppendLine($"**Kimleri etkiliyor:** {OneLine(story.WhoIsAffected)}");
        }

        if (story.KeyPoints.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("**Kilit noktalar**");
            foreach (var point in story.KeyPoints.Where(NotBlank).Take(MaxKeyPoints))
            {
                text.AppendLine($"- {OneLine(point)}");
            }
        }

        if (story.Topics.Count > 0)
        {
            text.AppendLine();
            text.AppendLine($"**Konular:** {string.Join(", ", story.Topics.Where(NotBlank))}");
        }

        AppendSources(text, story);
        AppendComparisons(text, story);
        AppendCommitment(text, story);

        if (!string.IsNullOrWhiteSpace(story.PersonalNote))
        {
            text.AppendLine();
            text.AppendLine($"**Kaydederken düştüğüm not:** {OneLine(story.PersonalNote)}");
        }
    }

    /// <summary>
    /// The line that tells the mentor which of its three registers this story sits
    /// in. Without it the mentor has to infer "is this a fact" from prose, which is
    /// exactly the inference the app already made and stored.
    /// </summary>
    private static string EvidenceRegister(BriefStoryFile story)
    {
        var total = story.Sources.Count;
        var official = story.Sources.Count(s => s.IsOfficial);

        var trust = $"Güven {story.TrustScore}/100";
        if (!string.IsNullOrWhiteSpace(story.TrustExplanation))
        {
            trust += $" ({OneLine(story.TrustExplanation)})";
        }

        // The evidential marker outranks the source count, and has to: several
        // outlets carrying the same reported claim corroborate the report, not the
        // fact. Without this the register would tell the mentor "doğrulanmış kabul
        // et" two lines above the warning telling it the opposite.
        var register = (total, official, story.HasEvidentialClaim) switch
        {
            (0, _, _) => "Kaynak kaydı yok.",
            (_, _, true) =>
                $"{Count(total)}, ama metin iddiayı aktarıyor. Kaynak sayısı burada " +
                "teyit sayılmaz — aynı iddiayı tekrarlıyor olabilirler.",
            (1, 0, _) =>
                "Tek kaynak, resmî değil. Bu haberdeki her iddiayı \"kaynağın " +
                "aktardığına göre\" diye ele al.",
            (1, _, _) => "Tek kaynak, ama resmî. Doğrudan aktarabilirsin.",
            (_, 0, _) =>
                $"{total} kaynak aynı şeyi bildiriyor, resmî doğrulama yok. Olgu " +
                "gibi kullanabilirsin ama resmî açıklama beklenmediğini not düş.",
            _ => $"{total} kaynak doğruladı, {official}'i resmî. Doğrulanmış kabul et."
        };

        return $"**Kanıt kaydı:** {register} {trust}.";
    }

    private static string Count(int total) => total == 1 ? "Tek kaynak" : $"{total} kaynak";

    private static void AppendSources(StringBuilder text, BriefStoryFile story)
    {
        if (story.Sources.Count == 0)
        {
            return;
        }

        text.AppendLine();
        text.AppendLine("**Kaynaklar**");

        var ordered = story.Sources.OrderBy(s => s.PublishedAt).ToList();

        for (var index = 0; index < Math.Min(ordered.Count, MaxSources); index++)
        {
            var source = ordered[index];
            var badge = source.IsOfficial ? " · resmî" : string.Empty;

            // Who broke it, rather than a clock time: the timestamps are UTC and the
            // reader is not, so an hour printed here would be quietly wrong while
            // the ordering it stands in for is exactly right.
            var first = index == 0 && ordered.Count > 1 ? " · ilk veren" : string.Empty;

            text.AppendLine(
                $"- {OneLine(source.Name)}{badge}{first} · {FormatDate(source.PublishedAt)}");
        }

        var hidden = story.Sources.Count - MaxSources;
        if (hidden > 0)
        {
            text.AppendLine($"- *(+{hidden} kaynak daha)*");
        }
    }

    private static void AppendComparisons(StringBuilder text, BriefStoryFile story)
    {
        var points = story.Comparisons.Where(p => NotBlank(p.Text)).Take(MaxComparisons).ToList();
        if (points.Count == 0)
        {
            return;
        }

        text.AppendLine();
        text.AppendLine("**Kaynaklar ne diyor**");

        foreach (var point in points)
        {
            var label = ComparisonLabels.TryGetValue(point.Kind, out var name) ? name : "not";
            text.AppendLine($"- [{label}] {OneLine(point.Text)}");

            if (NotBlank(point.Quote))
            {
                var attribution = NotBlank(point.QuoteSource) ? $" — {OneLine(point.QuoteSource)}" : string.Empty;
                text.AppendLine($"  > \"{Clip(OneLine(point.Quote), MaxQuoteLength)}\"{attribution}");
            }
        }
    }

    private static void AppendCommitment(StringBuilder text, BriefStoryFile story)
    {
        if (story.Commitment is not { } commitment)
        {
            return;
        }

        var tier = TierLabels.TryGetValue(commitment.Tier, out var label) ? label : "sınıflandırılmadı";

        text.AppendLine();
        text.Append($"**Taahhüt derecesi:** {tier}");

        if (NotBlank(commitment.DateText))
        {
            text.Append($" · {OneLine(commitment.DateText)}");
        }

        text.AppendLine();

        if (NotBlank(commitment.Event))
        {
            text.AppendLine($"**Olay:** {OneLine(commitment.Event)}");
        }

        if (NotBlank(commitment.Condition))
        {
            text.AppendLine($"**Şart:** {OneLine(commitment.Condition)}");
        }

        if (NotBlank(commitment.Reference))
        {
            text.AppendLine($"**Dayanak:** {OneLine(commitment.Reference)}");
        }

        if (NotBlank(commitment.Quote))
        {
            text.AppendLine($"> \"{Clip(OneLine(commitment.Quote), MaxQuoteLength)}\"");
        }

        if (commitment.IsReversed)
        {
            text.AppendLine(
                "> ⚠ Bu taahhüt sonradan **geri alındı / iptal edildi**. Yürürlükteymiş gibi anlatma.");
        }
    }

    private static void AppendPlan(StringBuilder text, BriefPlan? plan)
    {
        var questions = plan?.AnchorQuestions.Where(NotBlank).Take(MaxAnchorQuestions).ToList() ?? [];

        var hasNotes =
            NotBlank(plan?.DemoIdea) ||
            NotBlank(plan?.DiagramIdea) ||
            NotBlank(plan?.CommonMistake) ||
            NotBlank(plan?.OpenQuestion);

        if (questions.Count == 0 && !hasNotes)
        {
            return;
        }

        text.AppendLine();
        text.AppendLine("---");
        text.AppendLine();
        text.AppendLine("## Bu derste");

        if (questions.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("**Çapa sorular** — hepsini birden sorma, sırayla ilerle:");
            var index = 1;
            foreach (var question in questions)
            {
                text.AppendLine($"{index++}. {OneLine(question)}");
            }
        }

        if (NotBlank(plan?.DemoIdea))
        {
            text.AppendLine();
            text.AppendLine($"**Demo fikri:** {OneLine(plan!.DemoIdea)}");
        }

        if (NotBlank(plan?.DiagramIdea))
        {
            text.AppendLine();
            text.AppendLine($"**Şema fikri:** {OneLine(plan!.DiagramIdea)}");
        }

        if (NotBlank(plan?.CommonMistake))
        {
            text.AppendLine();
            text.AppendLine($"**Muhtemel yanlış anlamam:** {OneLine(plan!.CommonMistake)}");
        }

        if (NotBlank(plan?.OpenQuestion))
        {
            text.AppendLine();
            text.AppendLine($"**Dosyanın cevaplamadığı:** {OneLine(plan!.OpenQuestion)}");
        }
    }

    private static void AppendClosing(StringBuilder text, BriefPlan? plan)
    {
        text.AppendLine();
        text.AppendLine("---");
        text.AppendLine();
        text.AppendLine("## Nasıl başla");
        text.AppendLine();
        text.AppendLine(
            $"{FocusMentorPersona.Name} kurallarınla ilerle: üç satır fundamental, bir şema, " +
            "sonra seviye menüsü. Menüyü verdikten sonra **dur** ve benim seviye seçmemi bekle.");

        if (plan is null)
        {
            // Said out loud rather than silently shipping a thinner file: the reader
            // should know whether the questions are missing because the topic had
            // none or because the planner was unavailable.
            text.AppendLine();
            text.AppendLine(
                "*(Bu dosya çapa sorular olmadan üretildi — plan modeli yanıt vermedi. " +
                "Giriş seviyesini kendin öner, ama yine de sor.)*");
        }
    }

    private static string FormatDate(DateTimeOffset value)
    {
        var local = value.UtcDateTime;
        return $"{local.Day} {Months[local.Month - 1]} {local.Year}";
    }

    private static bool NotBlank(string? value) => !string.IsNullOrWhiteSpace(value);

    /// <summary>
    /// Collapses every run of whitespace to one space. Extracted article text
    /// arrives with hard line breaks in it, and a quote that wraps mid-line breaks
    /// out of the markdown block it was placed in.
    /// </summary>
    private static string OneLine(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var result = new StringBuilder(value.Length);
        var pendingSpace = false;

        foreach (var character in value)
        {
            if (char.IsWhiteSpace(character))
            {
                pendingSpace = result.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                result.Append(' ');
                pendingSpace = false;
            }

            result.Append(character);
        }

        return result.ToString();
    }

    private static string Clip(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..(maxLength - 1)].TrimEnd() + "…";
}
