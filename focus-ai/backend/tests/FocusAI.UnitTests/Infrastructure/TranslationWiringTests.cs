using FocusAI.Application;
using FocusAI.Application.Common.Interfaces;
using FocusAI.Infrastructure;
using FocusAI.Infrastructure.Ai;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FocusAI.UnitTests.Infrastructure;

/// <summary>
/// The translator is resolved through a factory that reads configuration, which is
/// exactly the kind of wiring that compiles cleanly and then throws on the first
/// request. These build the real container and ask for the real service.
/// </summary>
public class TranslationWiringTests
{
    private static ServiceProvider Build(params (string Key, string Value)[] settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Enough for AddInfrastructure to register; nothing here connects.
                ["ConnectionStrings:Postgres"] = "Host=localhost;Database=focusai;Username=focusai;Password=focusai",
                ["Jwt:SigningKey"] = "focus-ai-unit-test-signing-key-at-least-32-bytes"
            }.Concat(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
             .ToDictionary(kv => kv.Key, kv => kv.Value))
            .Build();

        return new ServiceCollection()
            .AddLogging()
            .AddApplication()
            .AddInfrastructure(configuration)
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    [Fact]
    public void With_no_configuration_the_translator_is_the_disabled_one()
    {
        using var provider = Build();
        using var scope = provider.CreateScope();

        var translator = scope.ServiceProvider.GetRequiredService<IContentTranslator>();

        // Off by default: the stack must stay runnable with zero extra containers.
        Assert.IsType<DisabledTranslator>(translator);
        Assert.False(translator.IsEnabled);
        Assert.Equal("none", translator.Backend);
    }

    [Fact]
    public void When_enabled_the_local_translator_resolves_with_its_http_client()
    {
        using var provider = Build(
            ("Translation:Enabled", "true"),
            ("Translation:BaseUrl", "http://translator:8080/v1/"));

        using var scope = provider.CreateScope();

        var translator = scope.ServiceProvider.GetRequiredService<IContentTranslator>();

        Assert.IsType<LocalTranslator>(translator);
        Assert.True(translator.IsEnabled);
    }

    [Fact]
    public async Task A_disabled_translator_returns_its_input_untouched()
    {
        using var provider = Build();
        using var scope = provider.CreateScope();

        var translator = scope.ServiceProvider.GetRequiredService<IContentTranslator>();
        string[] segments = ["English one.", "English two."];

        Assert.Equal(segments, await translator.TranslateAsync(segments, "en"));
    }

    [Fact]
    public void The_enrichment_handler_can_be_constructed()
    {
        // Catches the case where adding a constructor parameter to the handler is
        // not matched by a registration — MediatR would then fail at dispatch time.
        using var provider = Build(("Translation:Enabled", "true"));
        using var scope = provider.CreateScope();

        var handler = scope.ServiceProvider
            .GetRequiredService<MediatR.IRequestHandler<
                FocusAI.Application.Features.Ingestion.EnrichStoriesCommand,
                FocusAI.Application.Dtos.IngestionReportDto>>();

        Assert.NotNull(handler);
    }
}
