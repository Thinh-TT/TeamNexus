using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TeamNexus.Persistence;

namespace TeamNexus.Modules.Auth;

/// <summary>
/// DI registration entry point for the Auth module (modular monolith pattern).
/// Phase 1 §3 will add here: ASP.NET Core Identity, OAuth handlers (Google/GitHub)
/// and JwtService.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddAuthModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Single DbContext + migration chain shared by all modules.
        services.AddTeamNexusPersistence(configuration);

        // TODO(phase-1 §3): register Identity, OAuth (Google/GitHub), JwtService.
        return services;
    }
}
