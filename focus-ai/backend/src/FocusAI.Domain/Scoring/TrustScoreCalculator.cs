namespace FocusAI.Domain.Scoring;

/// <summary>Inputs for the 0-100 trust score described in PRD section 5.4.</summary>
public sealed record TrustScoreInput
{
    /// <summary>Distinct sources that carried the story ("kaç kaynak doğruladı").</summary>
    public required int DistinctSourceCount { get; init; }

    /// <summary>How many of those are first-party/official ("resmi kaynak").</summary>
    public required int OfficialSourceCount { get; init; }

    /// <summary>Highest editorial weight among contributing sources, in [0,1].</summary>
    public required double MaxSourceTrustWeight { get; init; }

    public required DateTimeOffset PublishedAt { get; init; }

    public required DateTimeOffset Now { get; init; }

    /// <summary>Summed community engagement across the cluster ("topluluk güveni").</summary>
    public int EngagementScore { get; init; }

    /// <summary>
    /// LLM verdict on claim specificity in [0,1] ("teknik doğruluk"): does the
    /// story cite versions, benchmarks and named APIs, or is it vibes? Null when
    /// no model has judged it yet, in which case a neutral prior is used.
    /// </summary>
    public double? TechnicalAccuracy { get; init; }
}

public sealed record TrustScoreResult(
    int OfficialSourceScore,
    int CorroborationScore,
    int RecencyScore,
    int TechnicalAccuracyScore,
    int CommunityScore,
    int Total,
    string Explanation);

/// <summary>
/// Turns the five PRD trust criteria into one number plus a breakdown. Pure and
/// deterministic — a score can always be explained and reproduced.
/// </summary>
public static class TrustScoreCalculator
{
    // Weights sum to 1.0. Official sourcing and corroboration dominate because
    // they are the two signals a reader cannot cheaply verify themselves.
    private const double OfficialWeight = 0.30;
    private const double CorroborationWeight = 0.25;
    private const double TechnicalWeight = 0.20;
    private const double CommunityWeight = 0.15;
    private const double RecencyWeight = 0.10;

    /// <summary>Neutral prior when no model has assessed technical accuracy.</summary>
    private const double DefaultTechnicalAccuracy = 0.6;

    /// <summary>Corroboration saturates here — the 9th source adds nothing the 8th did not.</summary>
    private const int CorroborationSaturation = 8;

    /// <summary>Engagement saturation point, roughly a front-page Hacker News thread.</summary>
    private const double EngagementSaturation = 1000d;

    public static TrustScoreResult Calculate(TrustScoreInput input)
    {
        var official = OfficialScore(input);
        var corroboration = CorroborationScore(input.DistinctSourceCount);
        var recency = RecencyScore(input.PublishedAt, input.Now);
        var technical = Clamp((input.TechnicalAccuracy ?? DefaultTechnicalAccuracy) * 100);
        var community = CommunityScore(input.EngagementScore);

        var total = Clamp(
            official * OfficialWeight +
            corroboration * CorroborationWeight +
            technical * TechnicalWeight +
            community * CommunityWeight +
            recency * RecencyWeight);

        return new TrustScoreResult(
            official,
            corroboration,
            recency,
            technical,
            community,
            total,
            BuildExplanation(input, total));
    }

    /// <summary>
    /// One official source already establishes the facts; extra ones add
    /// diminishing confirmation. With no official source we fall back to the
    /// best editorial weight available, capped well below the official floor.
    /// </summary>
    private static int OfficialScore(TrustScoreInput input)
    {
        if (input.OfficialSourceCount <= 0)
        {
            return Clamp(Math.Clamp(input.MaxSourceTrustWeight, 0d, 1d) * 60);
        }

        return Clamp(70 + Math.Min(30, (input.OfficialSourceCount - 1) * 10));
    }

    /// <summary>Log-scaled so 1→~33, 3→~65, 8→100.</summary>
    private static int CorroborationScore(int distinctSourceCount)
    {
        if (distinctSourceCount <= 0)
        {
            return 0;
        }

        var ratio = Math.Log2(1 + distinctSourceCount) / Math.Log2(1 + CorroborationSaturation);
        return Clamp(Math.Min(1d, ratio) * 100);
    }

    /// <summary>
    /// "Tarih" read as currency: a three-day-old story is still worth reading but
    /// is no longer news. Exponential decay with a 72-hour half-life, floored at
    /// 20 so archival stories never read as untrustworthy.
    /// </summary>
    private static int RecencyScore(DateTimeOffset publishedAt, DateTimeOffset now)
    {
        var hours = (now - publishedAt).TotalHours;
        if (hours <= 6)
        {
            return 100;
        }

        if (hours < 0)
        {
            // Feeds with wrong clocks publish "in the future"; treat as brand new.
            return 100;
        }

        var decayed = 100 * Math.Pow(0.5, hours / 72d);
        return Clamp(Math.Max(20d, decayed));
    }

    /// <summary>Log-scaled engagement, neutral-low (35) when a source reports none.</summary>
    private static int CommunityScore(int engagement)
    {
        if (engagement <= 0)
        {
            return 35;
        }

        var ratio = Math.Log10(1 + engagement) / Math.Log10(1 + EngagementSaturation);
        return Clamp(Math.Min(1d, ratio) * 100);
    }

    private static string BuildExplanation(TrustScoreInput input, int total)
    {
        var parts = new List<string>();

        if (input.OfficialSourceCount > 0)
        {
            parts.Add($"{input.OfficialSourceCount} resmi kaynak");
        }

        if (input.DistinctSourceCount > 1)
        {
            parts.Add($"{input.DistinctSourceCount} kaynak doğruladı");
        }
        else
        {
            parts.Add("tek kaynak");
        }

        var ageHours = (input.Now - input.PublishedAt).TotalHours;
        parts.Add(ageHours <= 24 ? "son 24 saat" : $"{(int)(ageHours / 24)} gün önce");

        var verdict = total switch
        {
            >= 85 => "Çok güvenilir",
            >= 70 => "Güvenilir",
            >= 50 => "Makul",
            >= 30 => "Temkinli yaklaş",
            _ => "Doğrulanmamış"
        };

        return $"{verdict} — {string.Join(", ", parts)}.";
    }

    private static int Clamp(double value) => (int)Math.Round(Math.Clamp(value, 0d, 100d));
}
