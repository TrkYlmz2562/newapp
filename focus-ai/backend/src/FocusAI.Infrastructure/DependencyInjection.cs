using FocusAI.Application.Common.Interfaces;
using FocusAI.Domain.Enums;
using FocusAI.Infrastructure.Ai;
using FocusAI.Infrastructure.Configuration;
using FocusAI.Infrastructure.Identity;
using FocusAI.Infrastructure.Ingestion;
using FocusAI.Infrastructure.Persistence;
using FocusAI.Infrastructure.Persistence.Interceptors;
using FocusAI.Infrastructure.Search;
using FocusAI.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FocusAI.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.Configure<LlmOptions>(configuration.GetSection(LlmOptions.SectionName));
        services.Configure<EmbeddingOptions>(configuration.GetSection(EmbeddingOptions.SectionName));
        services.Configure<SearchOptions>(configuration.GetSection(SearchOptions.SectionName));
        services.Configure<IngestionOptions>(configuration.GetSection(IngestionOptions.SectionName));
        services.Configure<Application.Common.Options.IngestionSettings>(
            configuration.GetSection(Application.Common.Options.IngestionSettings.SectionName));

        services.AddSingleton<IDateTimeProvider, DateTimeProvider>();
        services.AddScoped<AuditableEntityInterceptor>();

        AddPersistence(services, configuration);
        AddCaching(services, configuration);
        AddIdentity(services);
        AddAi(services);
        AddIngestion(services);
        AddSearch(services, configuration);

        services.AddScoped<IPushNotifier, LoggingPushNotifier>();
        services.AddScoped<DatabaseInitializer>();

        return services;
    }

    private static void AddPersistence(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Postgres")
                               ?? "Host=localhost;Port=5432;Database=focusai;Username=focusai;Password=focusai";

        services.AddDbContext<ApplicationDbContext>((provider, options) =>
        {
            options.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.UseVector();
                npgsql.EnableRetryOnFailure(3, TimeSpan.FromSeconds(5), null);
                npgsql.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName);
            });

            options.AddInterceptors(provider.GetRequiredService<AuditableEntityInterceptor>());
        });

        services.AddScoped<IApplicationDbContext>(provider =>
            provider.GetRequiredService<ApplicationDbContext>());

        services.AddScoped<IVectorSearch, PgVectorSearch>();
    }

    private static void AddCaching(IServiceCollection services, IConfiguration configuration)
    {
        var redis = configuration.GetConnectionString("Redis");

        if (string.IsNullOrWhiteSpace(redis))
        {
            // Same ICacheService contract either way, so nothing downstream cares.
            services.AddDistributedMemoryCache();
        }
        else
        {
            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = redis;
                options.InstanceName = "focusai:";
            });
        }

        services.AddScoped<ICacheService, CacheService>();
    }

    private static void AddIdentity(IServiceCollection services)
    {
        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddScoped<ITokenService, JwtTokenService>();
    }

    private static void AddAi(IServiceCollection services)
    {
        services.AddHttpClient(LlmClientFactory.HttpClientName)
            .AddStandardResilienceHandler(options =>
            {
                // Generation is slow and bursty; the default 30s attempt timeout
                // would abort perfectly healthy long completions.
                options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(120);
                options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(400);
                options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(240);
                options.Retry.MaxRetryAttempts = 2;
            });

        services.AddSingleton<ILlmClientFactory, LlmClientFactory>();
        services.AddScoped<IContentAiService, ContentAiService>();
        services.AddScoped<ICommitmentClassifier, CommitmentClassifier>();

        services.AddHttpClient<OpenAiEmbeddingService>((provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<EmbeddingOptions>>().Value;
            var baseUrl = options.BaseUrl ?? DefaultEmbeddingUrl(options.Provider);
            client.BaseAddress = new Uri(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/");
            client.Timeout = TimeSpan.FromSeconds(120);

            if (!string.IsNullOrWhiteSpace(options.ApiKey))
            {
                client.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", options.ApiKey);
            }
        });

        services.AddScoped<IEmbeddingService>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<EmbeddingOptions>>().Value;

            // No provider configured means local hashing: the pipeline stays
            // runnable with zero credentials, which is what makes `docker compose
            // up` produce a populated feed out of the box.
            var configured = options.Provider != LlmProviderKind.Disabled &&
                             (!string.IsNullOrWhiteSpace(options.ApiKey) ||
                              !string.IsNullOrWhiteSpace(options.BaseUrl));

            return configured
                ? provider.GetRequiredService<OpenAiEmbeddingService>()
                : new HashingEmbeddingService();
        });
    }

    private static void AddIngestion(IServiceCollection services)
    {
        services.AddHttpClient(IngestionServiceNames.HttpClient, (provider, client) =>
            {
                var options = provider.GetRequiredService<IOptions<IngestionOptions>>().Value;
                client.Timeout = TimeSpan.FromSeconds(Math.Clamp(options.HttpTimeoutSeconds, 5, 300));
                client.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
                client.DefaultRequestHeaders.Accept.ParseAdd(
                    "application/rss+xml, application/atom+xml, application/xml, application/json;q=0.9, */*;q=0.8");
            })
            .AddStandardResilienceHandler(options =>
            {
                options.Retry.MaxRetryAttempts = 2;
                options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(30);
                options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(100);
                options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(60);
            });

        // Registration order is resolution order: specific adapters first, then
        // the RSS catch-all.
        services.AddScoped<IFeedAdapter, HackerNewsAdapter>();
        services.AddScoped<IFeedAdapter, RedditAdapter>();
        services.AddScoped<IFeedAdapter, GitHubTrendingAdapter>();
        services.AddScoped<IFeedAdapter, RssFeedAdapter>();
        services.AddScoped<IFeedAdapterResolver, FeedAdapterResolver>();
        services.AddScoped<IContentExtractor, HttpContentExtractor>();
    }

    private static void AddSearch(IServiceCollection services, IConfiguration configuration)
    {
        var searchUrl = configuration[$"{SearchOptions.SectionName}:Url"];

        if (string.IsNullOrWhiteSpace(searchUrl))
        {
            services.AddScoped<ISearchIndex, NullSearchIndex>();
            return;
        }

        services.AddHttpClient(MeilisearchIndex.HttpClientName, (provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<SearchOptions>>().Value;
            var url = options.Url.EndsWith('/') ? options.Url : options.Url + "/";
            client.BaseAddress = new Uri(url);
            client.Timeout = TimeSpan.FromSeconds(20);

            if (!string.IsNullOrWhiteSpace(options.ApiKey))
            {
                client.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", options.ApiKey);
            }
        });

        services.AddScoped<ISearchIndex, MeilisearchIndex>();
    }

    private static string DefaultEmbeddingUrl(LlmProviderKind provider) => provider switch
    {
        LlmProviderKind.Ollama => "http://localhost:11434/v1/",
        LlmProviderKind.VLlm => "http://localhost:8000/v1/",
        LlmProviderKind.LmStudio => "http://localhost:1234/v1/",
        _ => "https://api.openai.com/v1/"
    };
}
