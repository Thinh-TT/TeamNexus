using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace TeamNexus.Modules.Auth;

/// <summary>
/// DI registration entry point for the Auth module (modular monolith pattern).
/// Phase 1 §2–§3 will register here: TeamNexusDbContext (Identity), ASP.NET Core Identity,
/// OAuth handlers (Google/GitHub) and JwtService. Currently a placeholder so Program.cs
/// already wires modules through one extension method per module.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddAuthModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // TODO(phase-1 §2/§3): register DbContext, Identity, OAuth, JWT services.
        return services;
    }
}
