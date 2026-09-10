using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using TeamNexus.Modules.Auth.Endpoints;
using TeamNexus.Modules.Auth.Options;
using TeamNexus.Modules.Auth.Services;
using TeamNexus.Persistence;
using TeamNexus.Persistence.Data;
using TeamNexus.Persistence.Data.Entities;
using TeamNexus.Shared.Endpoints;
using AspNet.Security.OAuth.GitHub;

namespace TeamNexus.Modules.Auth;

/// <summary>
/// DI registration entry point for the Auth module (modular monolith pattern):
/// persistence (single DbContext), ASP.NET Core Identity, JWT bearer auth that reads
/// the access token from an HttpOnly cookie, OAuth providers (GitHub now, Google later)
/// and authorization policies (Phase 1 §3).
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddAuthModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Single DbContext + migration chain shared by all modules.
        services.AddTeamNexusPersistence(configuration);

        services.AddHttpContextAccessor();

        // ---- JWT options ---------------------------------------------------
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .Validate(o =>
                !string.IsNullOrWhiteSpace(o.SigningKey)
                && Encoding.UTF8.GetByteCount(o.SigningKey) >= 32,
                "Jwt:SigningKey must be at least 32 bytes. Set it via User Secrets.")
            .ValidateOnStart();

        // ---- ASP.NET Core Identity (users/roles backed by TeamNexusDbContext) --
        services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.SignIn.RequireConfirmedEmail = false;
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<TeamNexusDbContext>();

        // ---- Authentication -------------------------------------------------
        var jwtOptions = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
            ?? new JwtOptions();

        var authBuilder = services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            // Access token travels in an HttpOnly cookie; the JwtBearer middleware
            // picks it up from there instead of the Authorization header.
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwtOptions.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwtOptions.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(1),
                };

                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        // Primary: JWT in the HttpOnly cookie (REST calls).
                        var token = context.Request.Cookies[AuthConstants.AccessTokenCookie];

                        // SignalR: browser WebSocket clients cannot set the Authorization
                        // header, so the standard SignalR pattern passes the token as the
                        // access_token query parameter. Accept it for /hubs/* only so REST
                        // tokens never leak into URLs/logs.
                        if (string.IsNullOrEmpty(token)
                            && context.Request.Path.StartsWithSegments("/hubs"))
                        {
                            token = context.Request.Query["access_token"];
                        }

                        if (!string.IsNullOrEmpty(token))
                        {
                            context.Token = token;
                        }

                        return Task.CompletedTask;
                    },
                };
            })
            // Temporary cookie used only inside the external OAuth login flow.
            .AddCookie(AuthConstants.ExternalScheme, options =>
            {
                options.Cookie.Name = ".TeamNexus.External";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.ExpireTimeSpan = TimeSpan.FromMinutes(15);
                options.SlidingExpiration = true;
                options.Events = new CookieAuthenticationEvents
                {
                    OnRedirectToLogin = ctx =>
                    {
                        ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        return Task.CompletedTask;
                    },
                    OnRedirectToAccessDenied = ctx =>
                    {
                        ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                        return Task.CompletedTask;
                    },
                };
            });

        // ---- OAuth: GitHub (Phase 1 §3.1) ------------------------------------
        // Google comes later — same pattern with .AddGoogle(...) once its
        // credentials exist (Microsoft.AspNetCore.Authentication.Google is referenced).
        var githubClientId = configuration["Authentication:GitHub:ClientId"];
        var githubClientSecret = configuration["Authentication:GitHub:ClientSecret"];

        if (!string.IsNullOrWhiteSpace(githubClientId) && !string.IsNullOrWhiteSpace(githubClientSecret))
        {
            authBuilder.AddGitHub(options =>
            {
                options.ClientId = githubClientId;
                options.ClientSecret = githubClientSecret;
                options.SignInScheme = AuthConstants.ExternalScheme;
                options.CallbackPath = AuthConstants.GitHubCallbackPath;
                options.Scope.Add("user:email"); // private e-mail addresses
            });
        }

        // ---- Authorization policies (RBAC, Phase 1 §3.3) --------------------
        services.AddAuthorization(options =>
        {
            options.AddPolicy(AuthConstants.AdminOnlyPolicy, policy =>
                policy.RequireClaim(ClaimTypes.Role, "Admin"));

            options.AddPolicy(AuthConstants.ManagerOrAbovePolicy, policy =>
                policy.RequireClaim(ClaimTypes.Role, "Admin", "Manager"));

            options.AddPolicy(AuthConstants.MemberOrAbovePolicy, policy =>
                policy.RequireClaim(ClaimTypes.Role, "Admin", "Manager", "Member"));
        });

        // ---- Anti-CSRF -------------------------------------------------------
        services.AddAntiforgery(options =>
        {
            options.HeaderName = AuthConstants.XsrfRequestHeader;
            options.Cookie.Name = "TeamNexus.Antiforgery";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        });

        // ---- App services ----------------------------------------------------
        services.AddSingleton<JwtService>();
        services.AddScoped<AuthService>();
        services.AddScoped<TokenCookieService>();
        services.AddTransient<AntiforgeryValidationEndpointFilter>();

        return services;
    }
}
