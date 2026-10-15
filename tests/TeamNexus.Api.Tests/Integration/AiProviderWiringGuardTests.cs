using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TeamNexus.Api.Tests.Infrastructure;
using TeamNexus.Modules.Ai.Options;
using TeamNexus.Modules.Ai.Services;
using TeamNexus.Modules.Ai.Services.Jev;

namespace TeamNexus.Api.Tests.Integration;

/// <summary>
/// Guards the test host's AI provider wiring against a **whole class of silent, money-spending bug**.
/// <para>
/// The failure this exists for: <c>WebApplicationFactory</c> appends <c>appsettings.Development.json</c> and
/// <b>User Secrets</b> to the configuration <i>after</i> <c>UseSetting</c>, so those sources win. A developer
/// with <c>Jev:ApiKey</c> in secrets — which is exactly how the phase documents configuring it — therefore
/// ran the Jev integration suites against the <b>live OpenRouter endpoint</b>. The tests still ran, still
/// "tested something", and silently spent real tokens on non-deterministic responses; the only visible
/// symptom was assertions failing for reasons no fixture had chosen.
/// </para>
/// <para>
/// <c>DeepSeek:ApiKey</c>, <c>Tavily:ApiKey</c> and <c>Email:ApiKey</c> already had the one-space guard.
/// <c>Jev:ApiKey</c> was added later without it. These tests assert the outcome rather than the mechanism, so
/// they keep working if the guard is ever implemented differently.
/// </para>
/// </summary>
public sealed class AiProviderWiringGuardTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _database;

    public AiProviderWiringGuardTests(DatabaseFixture database)
    {
        _database = database;
    }

    [Fact]
    public async Task TestHost_NeverResolvesTheRealJevProvider()
    {
        await using var scenario = await _database.CreateScenarioAsync();

        var provider = scenario.Factory.Services
            .CreateScope().ServiceProvider.GetRequiredService<IJevDecisionProvider>();

        Assert.IsType<FakeJevDecisionProvider>(provider);
    }

    [Fact]
    public async Task TestHost_ReportsNoJevApiKeySoTheOfflineProviderIsSelected()
    {
        // Asserted directly as well as through the resolved type: `HasApiKey` is the single switch the DI
        // registration reads, so a future provider factory cannot smuggle a real one past this.
        await using var scenario = await _database.CreateScenarioAsync();

        var options = scenario.Factory.Services
            .CreateScope().ServiceProvider.GetRequiredService<IOptions<JevOptions>>().Value;

        Assert.False(options.HasApiKey);
    }

    [Fact]
    public async Task TestHost_NeverResolvesTheRealDeepSeekOrEmailProviders()
    {
        // The pre-existing guards, asserted for the same reason: they are one line each and nothing else
        // protects them from a well-meaning refactor of ConfigureWebHost.
        await using var scenario = await _database.CreateScenarioAsync();

        var services = scenario.Factory.Services.CreateScope().ServiceProvider;

        Assert.IsType<FakeAiProvider>(services.GetRequiredService<IAiProvider>());

        // Read through IOptions, not the POCO itself: the module binds these sections with
        // `services.AddOptions<T>().Bind(...)`, which registers `IOptions<T>` rather than `T`.
        var email = services.GetRequiredService<IOptions<EmailOptions>>().Value;
        Assert.False(email.HasApiKey);
    }
}
