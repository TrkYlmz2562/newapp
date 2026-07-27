using FocusAI.Domain.Enums;
using FocusAI.Domain.Learning;
using Xunit;

namespace FocusAI.UnitTests.Learning;

public class BriefComposerTests
{
    private static readonly DateTimeOffset Published = new(2026, 7, 26, 9, 0, 0, TimeSpan.Zero);

    private static BriefStoryFile Story(
        int trustScore = 70,
        bool evidential = false,
        IReadOnlyList<BriefSourceRef>? sources = null) =>
        new()
        {
            Title = "Anthropic MCP sunucularını uzaktan çalıştırmayı açtı",
            Summary = "Model Context Protocol sunucuları artık uzak makinede barındırılabiliyor.",
            PublishedAt = Published,
            TrustScore = trustScore,
            HasEvidentialClaim = evidential,
            Sources = sources ?? [new BriefSourceRef("Anthropic", true, Published)]
        };

    private static BriefCaseFile File(params BriefStoryFile[] stories) =>
        new() { Title = "MCP uzak sunucular", Stories = stories, Minutes = 15 };

    [Fact]
    public void The_same_case_file_always_renders_the_same_text()
    {
        var file = File(Story());

        Assert.Equal(BriefComposer.Compose(file), BriefComposer.Compose(file));
    }

    [Fact]
    public void A_single_unofficial_source_is_marked_as_a_claim_not_a_fact()
    {
        var prompt = BriefComposer.Compose(
            File(Story(sources: [new BriefSourceRef("Bir blog", false, Published)])));

        Assert.Contains("Tek kaynak, resmî değil", prompt);
        Assert.Contains("aktardığına göre", prompt);
    }

    [Fact]
    public void Corroboration_by_an_official_source_reads_as_verified()
    {
        var prompt = BriefComposer.Compose(File(Story(sources:
        [
            new BriefSourceRef("Anthropic", true, Published),
            new BriefSourceRef("The Verge", false, Published.AddHours(2)),
            new BriefSourceRef("Ars Technica", false, Published.AddHours(3))
        ])));

        Assert.Contains("3 kaynak doğruladı, 1'i resmî", prompt);
        Assert.Contains("Doğrulanmış kabul et", prompt);
    }

    [Fact]
    public void Corroboration_without_an_official_source_says_so()
    {
        var prompt = BriefComposer.Compose(File(Story(sources:
        [
            new BriefSourceRef("The Verge", false, Published),
            new BriefSourceRef("Ars Technica", false, Published.AddHours(1))
        ])));

        Assert.Contains("resmî doğrulama yok", prompt);
        Assert.DoesNotContain("Doğrulanmış kabul et", prompt);
    }

    [Fact]
    public void A_reported_claim_carries_the_evidential_warning()
    {
        var prompt = BriefComposer.Compose(File(Story(evidential: true)));

        Assert.Contains("-mış kipi", prompt);
        Assert.Contains("teyit edilmemiş", prompt);
    }

    [Fact]
    public void A_story_that_asserts_rather_than_reports_carries_no_warning() =>
        Assert.DoesNotContain("-mış kipi", BriefComposer.Compose(File(Story(evidential: false))));

    [Fact]
    public void Quotes_survive_the_line_breaks_in_extracted_article_text()
    {
        var prompt = BriefComposer.Compose(File(Story() with
        {
            Comparisons =
            [
                new BriefComparisonPoint(
                    "Rakam konusunda ayrışıyorlar",
                    ComparisonKind.Divergent,
                    "Şirket\n  yatırımın\n\n20 milyon dolar olduğunu\tsöyledi",
                    "Bloomberg")
            ]
        }));

        Assert.Contains("\"Şirket yatırımın 20 milyon dolar olduğunu söyledi\" — Bloomberg", prompt);
    }

    [Fact]
    public void A_reversed_commitment_is_never_presented_as_live()
    {
        var prompt = BriefComposer.Compose(File(Story() with
        {
            Commitment = new BriefCommitmentRef(
                CommitmentTier.OfficialCommitment,
                "Fabrika yatırımı",
                "3. çeyrek 2026",
                "Yatırım kararı alınmıştır.",
                "KAP, 12 Temmuz 2026",
                Condition: null,
                IsReversed: true)
        }));

        Assert.Contains("resmî taahhüt", prompt);
        Assert.Contains("geri alındı", prompt);
    }

    [Fact]
    public void The_readers_own_note_reaches_the_mentor()
    {
        var prompt = BriefComposer.Compose(
            File(Story() with { PersonalNote = "Bunu kendi ajanımda kullanabilir miyim?" }));

        Assert.Contains("Bunu kendi ajanımda kullanabilir miyim?", prompt);
    }

    [Fact]
    public void Without_a_plan_the_brief_admits_the_planner_did_not_run()
    {
        var prompt = BriefComposer.Compose(File(Story()));

        Assert.Contains("plan modeli yanıt vermedi", prompt);
        Assert.DoesNotContain("Çapa sorular", prompt);
    }

    [Fact]
    public void With_a_plan_the_anchor_questions_are_numbered_and_the_apology_is_gone()
    {
        var prompt = BriefComposer.Compose(
            File(Story()),
            new BriefPlan
            {
                LearningGoal = "Uzak MCP sunucusunun yerelden farkını anlamak",
                EntryLevel = 1,
                EntryReason = "Protokolü duymuşsun ama kurmamışsın",
                AnchorQuestions = ["Bir tool çağrısı nereden nereye gidiyor?", "Yetki nerede kontrol ediliyor?"],
                DemoIdea = "Tek tool'lu bir MCP sunucusu, 30 satır",
                CommonMistake = "MCP'yi REST API sanmak"
            });

        Assert.Contains("1. Bir tool çağrısı nereden nereye gidiyor?", prompt);
        Assert.Contains("2. Yetki nerede kontrol ediliyor?", prompt);
        Assert.Contains("S1 · Nasıl çalışır", prompt);
        Assert.Contains("MCP'yi REST API sanmak", prompt);
        Assert.DoesNotContain("plan modeli yanıt vermedi", prompt);
    }

    [Fact]
    public void The_mentor_is_told_not_to_recite_the_summary_back()
    {
        var prompt = BriefComposer.Compose(File(Story()));

        Assert.Contains("özetini bana geri anlatma", prompt);
        Assert.Contains("seviye menüsü", prompt);
    }

    [Fact]
    public void Several_stories_each_get_their_own_section()
    {
        var prompt = BriefComposer.Compose(File(
            Story(),
            Story() with { Title = "OpenAI ajan çerçevesini yayımladı" }));

        Assert.Contains("## Anthropic MCP sunucularını uzaktan çalıştırmayı açtı", prompt);
        Assert.Contains("## OpenAI ajan çerçevesini yayımladı", prompt);
    }

    [Fact]
    public void A_long_source_list_is_capped_and_says_how_many_it_dropped()
    {
        var many = Enumerable
            .Range(0, 20)
            .Select(i => new BriefSourceRef($"Kaynak {i}", false, Published.AddMinutes(i)))
            .ToList();

        var prompt = BriefComposer.Compose(File(Story(sources: many)));

        Assert.Contains("(+8 kaynak daha)", prompt);
    }

    [Fact]
    public void The_outlet_that_broke_it_is_named_as_such()
    {
        var prompt = BriefComposer.Compose(File(Story(sources:
        [
            new BriefSourceRef("Ars Technica", false, Published.AddHours(6)),
            new BriefSourceRef("The Verge", false, Published)
        ])));

        Assert.Contains("- The Verge · ilk veren", prompt);
        Assert.DoesNotContain("Ars Technica · ilk veren", prompt);
    }

    [Fact]
    public void A_lone_source_is_not_labelled_as_the_one_that_broke_it() =>
        Assert.DoesNotContain("ilk veren", BriefComposer.Compose(File(Story())));

    [Fact]
    public void Dates_are_written_in_Turkish()
    {
        var prompt = BriefComposer.Compose(File(Story()));

        Assert.Contains("26 Temmuz 2026", prompt);
    }
}
