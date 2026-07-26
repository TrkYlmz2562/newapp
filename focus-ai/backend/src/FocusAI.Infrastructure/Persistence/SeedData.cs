using FocusAI.Domain.Entities.Content;
using FocusAI.Domain.Entities.Gamification;
using FocusAI.Domain.Enums;

namespace FocusAI.Infrastructure.Persistence;

/// <summary>
/// The starting catalogue: every source named in PRD section 6, the topic
/// vocabulary that drives interests and trends, and the badge set.
/// </summary>
/// <remarks>
/// <see cref="Source.TrustWeight"/> is the editorial judgement encoded here.
/// First-party vendor blogs sit at 0.9-0.95 because they *are* the primary
/// record; aggregators and community boards sit lower because they carry
/// unverified claims as readily as verified ones.
/// </remarks>
public static class SeedData
{
    public static IReadOnlyList<Source> Sources() =>
    [
        // ── Resmi bloglar ────────────────────────────────────────────────────
        Official("OpenAI Blog", "openai", "https://openai.com/news/", "https://openai.com/news/rss.xml", ContentCategory.Ai, 0.95),
        Official("Anthropic News", "anthropic", "https://www.anthropic.com/news", "https://www.anthropic.com/news/rss.xml", ContentCategory.Ai, 0.95),
        Official("Google DeepMind", "deepmind", "https://deepmind.google/discover/blog/", "https://deepmind.google/blog/rss.xml", ContentCategory.Ai, 0.93),
        Official("Google Research", "google-research", "https://research.google/blog/", "https://research.google/blog/rss/", ContentCategory.Ai, 0.9),
        Official("Microsoft .NET Blog", "dotnet-blog", "https://devblogs.microsoft.com/dotnet/", "https://devblogs.microsoft.com/dotnet/feed/", ContentCategory.Software, 0.95),
        Official("Azure Updates", "azure-blog", "https://azure.microsoft.com/en-us/blog/", "https://azure.microsoft.com/en-us/blog/feed/", ContentCategory.Tools, 0.92),
        Official("AWS News Blog", "aws", "https://aws.amazon.com/blogs/aws/", "https://aws.amazon.com/blogs/aws/feed/", ContentCategory.Tools, 0.92),
        Official("Cloudflare Blog", "cloudflare", "https://blog.cloudflare.com/", "https://blog.cloudflare.com/rss/", ContentCategory.Tools, 0.9),
        Official("Vercel Blog", "vercel", "https://vercel.com/blog", "https://vercel.com/atom", ContentCategory.Software, 0.88),
        Official("GitHub Blog", "github-blog", "https://github.blog/", "https://github.blog/feed/", ContentCategory.OpenSource, 0.9),
        Official("Docker Blog", "docker", "https://www.docker.com/blog/", "https://www.docker.com/blog/feed/", ContentCategory.Tools, 0.88),
        Official("JetBrains Blog", "jetbrains", "https://blog.jetbrains.com/", "https://blog.jetbrains.com/feed/", ContentCategory.Tools, 0.88),
        Official("Apple Developer News", "apple-developer", "https://developer.apple.com/news/", "https://developer.apple.com/news/rss/news.rss", ContentCategory.Software, 0.93),
        Official("Android Developers Blog", "android-developers", "https://android-developers.googleblog.com/", "https://android-developers.googleblog.com/feeds/posts/default", ContentCategory.Software, 0.92),
        Official("Meta AI", "meta-ai", "https://ai.meta.com/blog/", "https://ai.meta.com/blog/rss/", ContentCategory.Ai, 0.9),
        Official("NVIDIA Developer Blog", "nvidia", "https://developer.nvidia.com/blog/", "https://developer.nvidia.com/blog/feed/", ContentCategory.Hardware, 0.9),
        Official("Hugging Face Blog", "huggingface", "https://huggingface.co/blog", "https://huggingface.co/blog/feed.xml", ContentCategory.Ai, 0.87),

        // ── Topluluk ─────────────────────────────────────────────────────────
        Community("Hacker News", "hackernews", "https://news.ycombinator.com/", "https://news.ycombinator.com/", SourceKind.HackerNews, ContentCategory.Software, 0.65, 20),
        Community("r/programming", "reddit-programming", "https://www.reddit.com/r/programming/", "https://www.reddit.com/r/programming", SourceKind.Reddit, ContentCategory.Software, 0.55, 45),
        Community("r/MachineLearning", "reddit-ml", "https://www.reddit.com/r/MachineLearning/", "https://www.reddit.com/r/MachineLearning", SourceKind.Reddit, ContentCategory.Ai, 0.6, 45),
        Community("r/LocalLLaMA", "reddit-localllama", "https://www.reddit.com/r/LocalLLaMA/", "https://www.reddit.com/r/LocalLLaMA", SourceKind.Reddit, ContentCategory.Ai, 0.55, 45),
        Community("r/dotnet", "reddit-dotnet", "https://www.reddit.com/r/dotnet/", "https://www.reddit.com/r/dotnet", SourceKind.Reddit, ContentCategory.Software, 0.55, 60),
        Community("Dev.to", "devto", "https://dev.to/", "https://dev.to/feed", SourceKind.Rss, ContentCategory.Software, 0.45, 60),
        Community("Product Hunt", "producthunt", "https://www.producthunt.com/", "https://www.producthunt.com/feed", SourceKind.ProductHunt, ContentCategory.Product, 0.5, 120),

        // ── Kod ──────────────────────────────────────────────────────────────
        Code("GitHub Trending", "github-trending", "https://github.com/trending", "", SourceKind.GitHubTrending, ContentCategory.OpenSource, 0.7, 240),
        Code(".NET Releases", "dotnet-releases", "https://github.com/dotnet/core/releases", "https://github.com/dotnet/core/releases.atom", SourceKind.GitHubReleases, ContentCategory.Software, 0.95, 180),
        Code("Angular Releases", "angular-releases", "https://github.com/angular/angular/releases", "https://github.com/angular/angular/releases.atom", SourceKind.GitHubReleases, ContentCategory.Software, 0.95, 180),
        Code("Kubernetes Releases", "kubernetes-releases", "https://github.com/kubernetes/kubernetes/releases", "https://github.com/kubernetes/kubernetes/releases.atom", SourceKind.GitHubReleases, ContentCategory.Tools, 0.95, 240),

        // ── Akademik ─────────────────────────────────────────────────────────
        Academic("arXiv cs.AI", "arxiv-ai", "https://arxiv.org/list/cs.AI/recent", "https://rss.arxiv.org/rss/cs.AI", ContentCategory.Science, 0.8),
        Academic("arXiv cs.LG", "arxiv-lg", "https://arxiv.org/list/cs.LG/recent", "https://rss.arxiv.org/rss/cs.LG", ContentCategory.Science, 0.8),
        Academic("arXiv cs.SE", "arxiv-se", "https://arxiv.org/list/cs.SE/recent", "https://rss.arxiv.org/rss/cs.SE", ContentCategory.Science, 0.8),
        Academic("MIT News — Technology", "mit-news", "https://news.mit.edu/topic/technology", "https://news.mit.edu/rss/topic/artificial-intelligence2", ContentCategory.Science, 0.85),

        // ── Video ────────────────────────────────────────────────────────────
        Video("Google for Developers", "youtube-google-dev", "https://www.youtube.com/@GoogleDevelopers", "https://www.youtube.com/feeds/videos.xml?channel_id=UC_x5XG1OV2P6uZZ5FSM9Ttw", ContentCategory.Software),
        Video("Microsoft Developer", "youtube-msdev", "https://www.youtube.com/@MicrosoftDeveloper", "https://www.youtube.com/feeds/videos.xml?channel_id=UCsMica-v34Irf9KVTh6xx-g", ContentCategory.Software),
        Video("NDC Conferences", "youtube-ndc", "https://www.youtube.com/@NDC", "https://www.youtube.com/feeds/videos.xml?user=NDCConferences", ContentCategory.Software),

        // ── Finans: birincil kaynaklar ───────────────────────────────────────
        // Bu bölümün omurgası. Bir kurumun kendi duyurusu, ikinci elden yorum
        // haberinden yapısal olarak daha kesindir — Finans akışının "bağlayıcı
        // belge" seviyesi pratikte buradan gelir.
        // NOT: TCMB'nin ayrı "PPK Kararları" akışı Aralık 2025'te güncellenmeyi
        // sessizce bıraktı; faiz kararları Basın Duyuruları akışında geliyor.
        Official("TCMB Basın Duyuruları", "tcmb-duyuru", "https://www.tcmb.gov.tr/",
            "https://www.tcmb.gov.tr/wps/wcm/connect/TR/TCMB+TR/Bottom+Menu/Diger/RSS/Basin+Duyurulari",
            ContentCategory.Finance, 0.98),
        Official("TCMB Veri Duyuruları", "tcmb-veri", "https://www.tcmb.gov.tr/",
            "https://www.tcmb.gov.tr/wps/wcm/connect/EN/TCMB+EN/Bottom+Menu/Other/RSS/Data",
            ContentCategory.Finance, 0.98),
        Official("ECB Press", "ecb-press", "https://www.ecb.europa.eu/",
            "https://www.ecb.europa.eu/rss/press.html", ContentCategory.Finance, 0.97),
        Official("Federal Reserve — Monetary Policy", "fed-monetary", "https://www.federalreserve.gov/",
            "https://www.federalreserve.gov/feeds/press_monetary.xml", ContentCategory.Finance, 0.97),
        Official("SEC Press Releases", "sec-press", "https://www.sec.gov/",
            "https://www.sec.gov/news/pressreleases.rss", ContentCategory.Finance, 0.95),

        // ── Finans: haber ────────────────────────────────────────────────────
        // Reuters ve AP akışları kapatıldı (401/404). WSJ daha da sinsi: HTTP 200
        // dönüyor ama en yeni içeriği Ocak 2025 — durum koduna bakan bir hat
        // 18 aylık haberi taze sanıp yayımlar. Üçü de bilerek eklenmedi.
        News("Ekonomim", "ekonomim", "https://www.ekonomim.com/", "https://www.ekonomim.com/rss",
            ContentCategory.Finance, 0.7, 45, "tr"),
        News("AA Ekonomi", "aa-ekonomi", "https://www.aa.com.tr/tr/ekonomi",
            "https://www.aa.com.tr/tr/rss/default?cat=ekonomi", ContentCategory.Finance, 0.78, 45, "tr"),
        News("Webrazzi", "webrazzi", "https://webrazzi.com/", "https://webrazzi.com/feed",
            ContentCategory.Startup, 0.7, 60, "tr"),
        News("Bloomberg", "bloomberg", "https://www.bloomberg.com/",
            "https://feeds.bloomberg.com/markets/news.rss", ContentCategory.Finance, 0.8, 45),
        News("CNBC Earnings", "cnbc-earnings", "https://www.cnbc.com/earnings/",
            "https://search.cnbc.com/rs/search/combinedcms/view.xml?partnerId=wrss01&id=15839135",
            ContentCategory.Finance, 0.75, 60)
    ];

    public static IReadOnlyList<Topic> Topics() =>
    [
        // Diller
        Topic("C#", "csharp", TopicKind.Language, ContentCategory.Software, ["c sharp", "csharp"]),
        Topic("TypeScript", "typescript", TopicKind.Language, ContentCategory.Software, ["ts"]),
        Topic("JavaScript", "javascript", TopicKind.Language, ContentCategory.Software, ["js", "ecmascript"]),
        Topic("Python", "python", TopicKind.Language, ContentCategory.Software, ["py"]),
        Topic("Go", "golang", TopicKind.Language, ContentCategory.Software, ["golang"]),
        Topic("Rust", "rust", TopicKind.Language, ContentCategory.Software, []),
        Topic("Java", "java", TopicKind.Language, ContentCategory.Software, []),
        Topic("Swift", "swift", TopicKind.Language, ContentCategory.Software, []),
        Topic("Kotlin", "kotlin", TopicKind.Language, ContentCategory.Software, []),
        Topic("SQL", "sql", TopicKind.Language, ContentCategory.Software, ["t sql", "postgresql", "sql server"]),

        // Framework / platform
        Topic(".NET", "dotnet", TopicKind.Framework, ContentCategory.Software, ["dot net", "net 9", "net 10", "aspnet", "asp net core", "entity framework", "ef core", "blazor", "maui"]),
        Topic("Angular", "angular", TopicKind.Framework, ContentCategory.Software, ["angular 19", "angular 20", "rxjs", "ngrx"]),
        Topic("React", "react", TopicKind.Framework, ContentCategory.Software, ["reactjs", "react 19"]),
        Topic("Next.js", "nextjs", TopicKind.Framework, ContentCategory.Software, ["next js"]),
        Topic("Node.js", "nodejs", TopicKind.Framework, ContentCategory.Software, ["node js", "nodejs"]),
        Topic("Vue", "vue", TopicKind.Framework, ContentCategory.Software, ["vuejs", "nuxt"]),
        Topic("Spring", "spring", TopicKind.Framework, ContentCategory.Software, ["spring boot"]),

        // Altyapı
        Topic("Docker", "docker", TopicKind.Platform, ContentCategory.Tools, ["container", "containerization"]),
        Topic("Kubernetes", "kubernetes", TopicKind.Platform, ContentCategory.Tools, ["k8s", "kubectl"]),
        Topic("Azure", "azure", TopicKind.Platform, ContentCategory.Tools, ["microsoft azure", "azure devops"]),
        Topic("AWS", "aws", TopicKind.Platform, ContentCategory.Tools, ["amazon web services", "lambda", "s3"]),
        Topic("Google Cloud", "gcp", TopicKind.Platform, ContentCategory.Tools, ["google cloud", "gcp"]),
        Topic("PostgreSQL", "postgresql", TopicKind.Product, ContentCategory.Tools, ["postgres", "pgvector"]),
        Topic("Redis", "redis", TopicKind.Product, ContentCategory.Tools, ["valkey"]),
        Topic("CI/CD", "cicd", TopicKind.Concept, ContentCategory.Tools, ["github actions", "continuous integration", "continuous delivery", "devops"]),

        // Yapay zekâ
        Topic("Yapay Zekâ", "ai", TopicKind.Concept, ContentCategory.Ai, ["artificial intelligence", "yapay zeka", "machine learning", "makine ogrenmesi"]),
        Topic("LLM", "llm", TopicKind.Concept, ContentCategory.Ai, ["large language model", "buyuk dil modeli", "foundation model"]),
        Topic("AI Agent", "ai-agent", TopicKind.Concept, ContentCategory.Ai, ["agentic", "autonomous agent", "agent framework"]),
        Topic("MCP", "mcp", TopicKind.Concept, ContentCategory.Ai, ["model context protocol"]),
        Topic("RAG", "rag", TopicKind.Concept, ContentCategory.Ai, ["retrieval augmented generation", "vector search", "embedding"]),
        Topic("Prompt Engineering", "prompt-engineering", TopicKind.Concept, ContentCategory.Ai, ["prompting", "prompt design"]),
        Topic("Fine-tuning", "fine-tuning", TopicKind.Concept, ContentCategory.Ai, ["finetuning", "lora", "peft"]),
        Topic("OpenAI", "openai", TopicKind.Company, ContentCategory.Ai, ["gpt", "chatgpt", "o series"]),
        Topic("Anthropic", "anthropic", TopicKind.Company, ContentCategory.Ai, ["claude"]),
        Topic("Google", "google", TopicKind.Company, ContentCategory.Ai, ["gemini", "deepmind"]),
        Topic("Meta", "meta", TopicKind.Company, ContentCategory.Ai, ["llama", "facebook"]),
        Topic("NVIDIA", "nvidia", TopicKind.Company, ContentCategory.Hardware, ["cuda", "gpu"]),

        // Pratik
        Topic("Mobil", "mobile", TopicKind.Concept, ContentCategory.Software, ["ios", "android", "mobil"]),
        Topic("Güvenlik", "security", TopicKind.Concept, ContentCategory.Security, ["vulnerability", "cve", "guvenlik", "zero day"]),
        Topic("Performans", "performance", TopicKind.Concept, ContentCategory.Software, ["benchmark", "optimization", "performans"]),
        Topic("Mimari", "architecture", TopicKind.Concept, ContentCategory.Software, ["microservices", "clean architecture", "mimari", "system design"]),
        Topic("Açık Kaynak", "open-source", TopicKind.Concept, ContentCategory.OpenSource, ["opensource", "acik kaynak", "license"]),
        Topic("Kariyer", "career", TopicKind.Concept, ContentCategory.Career, ["hiring", "interview", "kariyer", "remote work"])
    ];

    public static IReadOnlyList<Badge> Badges() =>
    [
        new() { Slug = "first-week", Name = "İlk Hafta", Description = "Yedi gün boyunca Focus AI'ı kullandın.", Emoji = "🌱" },
        new() { Slug = "consistent-reader", Name = "İstikrarlı Okur", Description = "Bir ay boyunca haftalık hedefini tutturdun.", Emoji = "📚" },
        new() { Slug = "deep-diver", Name = "Derinlemesine", Description = "Yüz haberi sonuna kadar okudun.", Emoji = "🔍" },
        new() { Slug = "lifelong-learner", Name = "Sürekli Öğrenen", Description = "Yirmi öğrenme önerisini tamamladın.", Emoji = "🎓" },
        new() { Slug = "curator", Name = "Küratör", Description = "Elli haber kaydettin.", Emoji = "🔖" }
    ];

    private static Source Official(
        string name,
        string slug,
        string website,
        string feed,
        ContentCategory category,
        double trustWeight) =>
        new()
        {
            Name = name,
            Slug = slug,
            WebsiteUrl = website,
            FeedUrl = feed,
            Kind = SourceKind.Rss,
            Category = SourceCategory.OfficialBlog,
            DefaultContentCategory = category,
            IsOfficial = true,
            TrustWeight = trustWeight,
            FetchIntervalMinutes = 60
        };

    /// <summary>
    /// A news outlet: real reporting, but second-hand by construction, so it never
    /// counts as official and its items cannot on their own reach the binding tiers
    /// of the Finans feed.
    /// </summary>
    private static Source News(
        string name,
        string slug,
        string website,
        string feed,
        ContentCategory category,
        double trustWeight,
        int intervalMinutes,
        string language = "en") =>
        new()
        {
            Name = name,
            Slug = slug,
            WebsiteUrl = website,
            FeedUrl = feed,
            Kind = SourceKind.Rss,
            Category = SourceCategory.News,
            DefaultContentCategory = category,
            IsOfficial = false,
            TrustWeight = trustWeight,
            FetchIntervalMinutes = intervalMinutes,
            Language = language
        };

    private static Source Community(
        string name,
        string slug,
        string website,
        string feed,
        SourceKind kind,
        ContentCategory category,
        double trustWeight,
        int intervalMinutes) =>
        new()
        {
            Name = name,
            Slug = slug,
            WebsiteUrl = website,
            FeedUrl = feed,
            Kind = kind,
            Category = SourceCategory.Community,
            DefaultContentCategory = category,
            IsOfficial = false,
            TrustWeight = trustWeight,
            FetchIntervalMinutes = intervalMinutes
        };

    private static Source Code(
        string name,
        string slug,
        string website,
        string feed,
        SourceKind kind,
        ContentCategory category,
        double trustWeight,
        int intervalMinutes) =>
        new()
        {
            Name = name,
            Slug = slug,
            WebsiteUrl = website,
            FeedUrl = feed,
            Kind = kind,
            Category = SourceCategory.Code,
            DefaultContentCategory = category,
            // Release feeds are the vendor's own record, so they count as official.
            IsOfficial = kind == SourceKind.GitHubReleases,
            TrustWeight = trustWeight,
            FetchIntervalMinutes = intervalMinutes
        };

    private static Source Academic(
        string name,
        string slug,
        string website,
        string feed,
        ContentCategory category,
        double trustWeight) =>
        new()
        {
            Name = name,
            Slug = slug,
            WebsiteUrl = website,
            FeedUrl = feed,
            Kind = SourceKind.Arxiv,
            Category = SourceCategory.Academic,
            DefaultContentCategory = category,
            IsOfficial = false,
            TrustWeight = trustWeight,
            FetchIntervalMinutes = 360
        };

    private static Source Video(
        string name,
        string slug,
        string website,
        string feed,
        ContentCategory category) =>
        new()
        {
            Name = name,
            Slug = slug,
            WebsiteUrl = website,
            FeedUrl = feed,
            Kind = SourceKind.YouTubeRss,
            Category = SourceCategory.Video,
            DefaultContentCategory = category,
            IsOfficial = true,
            TrustWeight = 0.8,
            FetchIntervalMinutes = 360
        };

    private static Topic Topic(
        string name,
        string slug,
        TopicKind kind,
        ContentCategory category,
        string[] aliases) =>
        new()
        {
            Name = name,
            Slug = slug,
            Kind = kind,
            Category = category,
            Aliases = aliases.ToList(),
            IsSuggestable = true
        };
}
