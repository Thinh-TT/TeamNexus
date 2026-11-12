using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TeamNexus.Api.Tests.Infrastructure;
using TeamNexus.Persistence;
using TeamNexus.Persistence.Data;

namespace TeamNexus.Api.Tests.Integration;

/// <summary>
/// TASK-DP-01: the production key ring lives in PostgreSQL (<c>data_protection_keys</c>) because Render
/// free tier has no persistent disk. This suite proves the actual mechanism end-to-end against a real
/// PostgreSQL: a provider that protects a payload, then a brand-new provider/service-provider (the
/// equivalent of a restarted container) must be able to unprotect it by reading the same table.
/// <para>
/// It deliberately does not truncate <c>data_protection_keys</c>: the shared test host also uses the ring,
/// and <see cref="TestScenario"/>'s allow-list leaves the table alone. Extra key rows are tiny and are
/// cleaned with the test database itself.
/// </para>
/// </summary>
public sealed class DataProtectionKeyRingPersistenceTests : IClassFixture<DatabaseFixture>
{
    private const string Purpose = "TeamNexus.Tests.DataProtection.Persistence";

    private readonly DatabaseFixture _database;

    public DataProtectionKeyRingPersistenceTests(DatabaseFixture database)
    {
        _database = database;
    }

    [Fact]
    public void KeysPersistedInPostgres_AreUsableByASecondProvider()
    {
        _database.Require();

        const string secret = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

        string ciphertext;
        using (var first = BuildProvider())
        {
            ciphertext = first.GetRequiredService<IDataProtectionProvider>()
                .CreateProtector(Purpose)
                .Protect(secret);
        }

        // A new ServiceProvider is the closest in-process equivalent of a redeployed container:
        // it shares no in-memory key cache with the first provider.
        using (var second = BuildProvider())
        {
            var plaintext = second.GetRequiredService<IDataProtectionProvider>()
                .CreateProtector(Purpose)
                .Unprotect(ciphertext);

            Assert.Equal(secret, plaintext);
        }

        using var db = _database.NewDbContext();
        Assert.True(
            db.DataProtectionKeys.AsNoTracking().Any(),
            "DataProtection phải ghi key ring vào bảng data_protection_keys.");
    }

    private ServiceProvider BuildProvider()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _database.ConnectionString,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTeamNexusPersistence(configuration);
        services.AddDataProtection()
            .SetApplicationName("TeamNexus")
            .PersistKeysToDbContext<TeamNexusDbContext>();

        return services.BuildServiceProvider();
    }
}
