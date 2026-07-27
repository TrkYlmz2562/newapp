using FocusAI.Domain.Enums;

namespace FocusAI.Domain.Scoring;

/// <summary>Everything about a story that is known before a model has read it.</summary>
public sealed record EnrichmentGateInput
{
    public required int DistinctSourceCount { get; init; }

    public required int OfficialSourceCount { get; init; }

    /// <summary>Highest editorial weight among contributing sources, in [0,1].</summary>
    public required double MaxSourceTrustWeight { get; init; }

    public int EngagementScore { get; init; }

    /// <summary>
    /// When the story was published. The only clock the gate consults — it judges
    /// every story at <see cref="EnrichmentGate.ReferenceAge"/> past this, never at
    /// the wall clock, so two identical stories get the same answer whether they
    /// are enriched immediately or after a backlog.
    /// </summary>
    public required DateTimeOffset PublishedAt { get; init; }

    /// <summary>
    /// The story's own category once known, otherwise the contributing source's
    /// default. Only feeds the category multiplier, so a wrong guess moves the
    /// projection a little rather than deciding it.
    /// </summary>
    public ContentCategory Category { get; init; } = ContentCategory.Unknown;
}

/// <summary>
/// What a story would score if the model read it and found it perfectly ordinary.
/// </summary>
/// <remarks>
/// The feed has an importance floor, and until now every story was summarised and
/// judged — two model calls — before anyone asked whether it would clear it. Most
/// did not. This asks first.
///
/// The trick is that the floor is mostly decided by things no model is needed for.
/// Of the five weighted components in <see cref="ImportanceScoreCalculator"/>, only
/// one comes from the summariser; breadth of pickup, freshness, source trust and
/// community engagement are all known the moment the article is clustered. So the
/// projection is the real calculation with the model's two contributions — its
/// importance verdict and its technical-accuracy read — left at the neutral priors
/// the calculators already use when no model is configured.
///
/// That makes the rule easy to state: <b>a story is enriched when it would reach
/// the feed on an average verdict.</b> One that only qualifies if the model happens
/// to rate it highly does not get the chance, and that is a deliberate trade — a
/// genuine single-source scoop is dropped without ever being read. The alternative
/// is paying for every one of them to find it, which is what the feed was doing.
///
/// Not a ceiling test. Assuming the best possible verdict instead would reject
/// almost nothing: the model's share is 35 points, so a fresh story clears the
/// floor on optimism alone. The point is to stop gambling a call on optimism.
///
/// Deliberately shares the calculators rather than approximating them. A gate that
/// drifts from the bar it is guarding would suppress stories that the feed would
/// have shown, and no one would ever see them to notice.
/// </remarks>
public static class EnrichmentGate
{
    /// <summary>
    /// The age every story is judged at, whatever its real age.
    /// </summary>
    /// <remarks>
    /// Freshness and recency are a quarter of the score between them, and at the
    /// moment of enrichment every candidate has just arrived — so they hand out the
    /// same bonus to everything and discriminate between nothing. Measured: a lone
    /// blog post nobody picked up scores 52 while an hour old, comfortably over the
    /// feed's floor, and a story three outlets carried scores 48 once a day has
    /// passed. Left in, the gate would be a clock rather than an editor.
    ///
    /// Holding age constant removes that. What is left is the evidence: who else
    /// carried it, whether any of them are first-party, how much the source is
    /// trusted, and whether anyone is reading it. Real freshness still decides
    /// ranking afterwards, where it belongs — a story's position in the feed should
    /// depend on when it happened; whether it is worth reading at all should not.
    /// </remarks>
    public static readonly TimeSpan ReferenceAge = TimeSpan.FromHours(24);

    /// <summary>
    /// The projected score a story needs before it is worth a model call.
    /// </summary>
    /// <remarks>
    /// Deliberately a different number from <c>StoryFilters.MinImportance</c>,
    /// because it answers a different question against a different basis: the feed
    /// floor judges a finished score that contains the model's real verdict and the
    /// story's real age, while this judges evidence alone at a fixed age. Matching
    /// the two numbers would look tidy and mean nothing.
    ///
    /// Set where the measured cases separate. At 48 the gate turns away exactly one
    /// kind of story: a lone report that no other outlet picked up and that nobody
    /// is reading. Corroboration by a second outlet, a first-party source, or any
    /// real community interest all clear it. That is the whole editorial rule, and
    /// it is worth stating plainly because it is also the rule's cost — a genuine
    /// single-source scoop that arrives before anyone has noticed it is dropped
    /// unread. Clustering brings it back the moment a second outlet files.
    /// </remarks>
    public const int MinProjectedImportance = 48;

    /// <summary>Whether this story has earned a model call.</summary>
    public static bool IsWorthEnriching(EnrichmentGateInput input) =>
        ProjectedImportance(input) >= MinProjectedImportance;

    public static int ProjectedImportance(EnrichmentGateInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var sources = Math.Max(1, input.DistinctSourceCount);

        // Every story judged at the same age. `Now` is ignored on purpose.
        var published = input.PublishedAt;
        var at = published + ReferenceAge;

        var trust = TrustScoreCalculator.Calculate(new TrustScoreInput
        {
            DistinctSourceCount = sources,
            OfficialSourceCount = input.OfficialSourceCount,
            MaxSourceTrustWeight = input.MaxSourceTrustWeight,
            PublishedAt = published,
            Now = at,
            EngagementScore = input.EngagementScore,
            // No model has read it, so no verdict on how specific its claims are.
            TechnicalAccuracy = null
        });

        return ImportanceScoreCalculator.Calculate(new ImportanceScoreInput
        {
            TrustScore = trust.Total,
            DistinctSourceCount = sources,
            OfficialSourceCount = input.OfficialSourceCount,
            EngagementScore = input.EngagementScore,
            PublishedAt = published,
            Now = at,
            // Null is the neutral prior, which is exactly the assumption being made.
            LlmImportance = null,
            Category = input.Category
        });
    }
}
