using EFCore.NamingConventions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pgvector.EntityFrameworkCore;
using TeamNexus.Persistence.Data;

namespace TeamNexus.Persistence;

/// <summary>
/// DI entry point for persistence: registers the single <see cref="TeamNexusDbContext"/>
/// (Npgsql/PostgreSQL + snake_case naming). Called by feature modules, e.g. Auth's
/// <c>AddAuthModule</c>, which keeps Program.cs free of persistence details.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddTeamNexusPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "'ConnectionStrings:DefaultConnection' is not configured. "
                + "Set it via User Secrets or the ConnectionStrings__DefaultConnection env var.");

        services.AddDbContext<TeamNexusDbContext>(options =>
            options.UseNpgsql(
                       connectionString,
                       // Giai đoạn 14.7 Track B §B3: registers the pgvector type mappings, which is what lets
                       // `Vector?` map to a `vector(1536)` column and `.CosineDistance()` compile to `<#>`.
                       // Without this the provider would reject the Vector CLR type at model-build time.
                       npgsql => npgsql.UseVector())
                   .UseSnakeCaseNamingConvention());

        return services;
    }
}
