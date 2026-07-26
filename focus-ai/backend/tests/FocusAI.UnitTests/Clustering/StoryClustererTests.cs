using FocusAI.Domain.Clustering;
using FocusAI.Domain.Text;
using Xunit;

namespace FocusAI.UnitTests.Clustering;

public class StoryClustererTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 26, 12, 0, 0, TimeSpan.Zero);

    private static ClusterCandidate Candidate(
        string text,
        string? hash = null,
        float[]? embedding = null,
        int hoursAgo = 0) =>
        new()
        {
            ArticleId = Guid.NewGuid(),
            SourceId = Guid.NewGuid(),
            ContentHash = hash ?? Guid.NewGuid().ToString("N"),
            SimHash = SimHash.Compute(text),
            Embedding = embedding,
            PublishedAt = Now.AddHours(-hoursAgo)
        };

    private static ClusterTarget Target(
        string text,
        Guid? id = null,
        string? hash = null,
        float[]? embedding = null,
        int hoursAgo = 0) =>
        new()
        {
            StoryId = id ?? Guid.NewGuid(),
            SimHash = SimHash.Compute(text),
            Embedding = embedding,
            PublishedAt = Now.AddHours(-hoursAgo),
            LastActivityAt = Now.AddHours(-hoursAgo),
            ContentHashes = hash is null ? new HashSet<string>() : new HashSet<string> { hash }
        };

    [Fact]
    public void No_targets_means_a_new_story()
    {
        var decision = StoryClusterer.FindCluster(Candidate("OpenAI launches GPT-6"), []);

        Assert.False(decision.IsMatch);
        Assert.Equal(ClusterMatchKind.None, decision.Kind);
    }

    [Fact]
    public void Identical_content_hash_is_an_exact_match()
    {
        const string hash = "DEADBEEF";
        var storyId = Guid.NewGuid();

        var decision = StoryClusterer.FindCluster(
            Candidate("OpenAI launches GPT-6", hash),
            [Target("Something else entirely", storyId, hash)]);

        Assert.Equal(ClusterMatchKind.ExactHash, decision.Kind);
        Assert.Equal(storyId, decision.StoryId);
        Assert.Equal(1d, decision.Similarity);
    }

    [Fact]
    public void Reworded_headline_from_a_syndicating_outlet_is_a_near_duplicate()
    {
        const string original =
            "OpenAI releases GPT-6 with major improvements to function calling and tool use for developers";

        // Same wire copy, one word changed — the case SimHash exists to catch.
        const string syndicated =
            "OpenAI releases GPT-6 with major improvements to function calling and tool use for engineers";

        var storyId = Guid.NewGuid();
        var decision = StoryClusterer.FindCluster(Candidate(syndicated), [Target(original, storyId)]);

        Assert.Equal(ClusterMatchKind.NearDuplicate, decision.Kind);
        Assert.Equal(storyId, decision.StoryId);
    }

    [Fact]
    public void Unrelated_stories_are_not_merged()
    {
        var decision = StoryClusterer.FindCluster(
            Candidate("Postgres 18 ships asynchronous IO for sequential scans"),
            [Target("Apple announces new MacBook Pro with M6 chip and improved display")]);

        Assert.False(decision.IsMatch);
    }

    [Fact]
    public void Semantically_close_embeddings_merge_above_the_threshold()
    {
        var storyId = Guid.NewGuid();
        var baseVector = new float[] { 1f, 0.9f, 0.8f, 0.2f };
        var closeVector = new float[] { 0.98f, 0.92f, 0.79f, 0.22f };

        var decision = StoryClusterer.FindCluster(
            Candidate("Completely different words here", embedding: closeVector),
            [Target("Nothing alike in the text at all", storyId, embedding: baseVector)]);

        Assert.Equal(ClusterMatchKind.Semantic, decision.Kind);
        Assert.Equal(storyId, decision.StoryId);
    }

    [Fact]
    public void Distant_embeddings_stay_separate()
    {
        var decision = StoryClusterer.FindCluster(
            Candidate("Alpha bravo charlie", embedding: [1f, 0f, 0f, 0f]),
            [Target("Delta echo foxtrot", embedding: [0f, 0f, 0f, 1f])]);

        Assert.False(decision.IsMatch);
    }

    [Fact]
    public void Coverage_outside_the_window_starts_a_follow_up_story()
    {
        const string headline = "Kubernetes 1.35 released with in-place pod resize going stable";

        var decision = StoryClusterer.FindCluster(
            Candidate(headline, hoursAgo: 0),
            [Target(headline, hoursAgo: 24 * 30)]);

        // Same topic a month later is a follow-up, not the original event.
        Assert.False(decision.IsMatch);
    }

    [Fact]
    public void Long_running_story_keeps_absorbing_late_coverage()
    {
        const string headline = "GPT-6 rollout continues across enterprise customers worldwide";
        var storyId = Guid.NewGuid();

        var target = Target(headline, storyId, hoursAgo: 24 * 10) with
        {
            // Published ten days ago but still being written about yesterday.
            LastActivityAt = Now.AddHours(-24)
        };

        var decision = StoryClusterer.FindCluster(Candidate(headline, hoursAgo: 0), [target]);

        Assert.True(decision.IsMatch);
        Assert.Equal(storyId, decision.StoryId);
    }

    [Fact]
    public void Empty_simhashes_do_not_collide()
    {
        // Two items with unextractable text both hash to 0; treating that as a
        // match would merge every failed extraction into one story.
        var decision = StoryClusterer.FindCluster(
            Candidate(string.Empty),
            [Target(string.Empty)]);

        Assert.False(decision.IsMatch);
    }
}
