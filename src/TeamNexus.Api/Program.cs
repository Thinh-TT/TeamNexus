using Scalar.AspNetCore;
using TeamNexus.Modules.Auth;
using TeamNexus.Modules.Auth.Endpoints;
using TeamNexus.Modules.Board;
using TeamNexus.Modules.Board.Endpoints;
using TeamNexus.Modules.Board.Hubs;

var builder = WebApplication.CreateBuilder(args);

// =====================================================================
// Services (DI)
// =====================================================================
builder.Services.AddProblemDetails();

// OpenAPI document (.NET 10 built-in). UI served by Scalar at /scalar.
builder.Services.AddOpenApi();

// ---- Module registrations (Modular Monolith) -------------------------
// Each module exposes one extension method, e.g. builder.Services.AddAuthModule(...).
// Auth internals (DbContext, Identity, OAuth, JWT) arrive in Phase 1 §2–§3.
builder.Services.AddAuthModule(builder.Configuration);

// Board module: Kanban CRUD services + SignalR hub registration (Phase 2 §2–§3).
builder.Services.AddBoardModule();

// ---- CORS ------------------------------------------------------------
// Dev convenience: during Phase 1 the frontend (Vite, :5173) talks to this API
// through its dev proxy, so CORS is normally not hit. This policy still allows
// direct browser calls from the Vite origin (cookie auth needs AllowCredentials).
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    options.AddPolicy("WebFrontend", policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

var app = builder.Build();

// =====================================================================
// Middleware pipeline
// =====================================================================
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options =>
    {
        options.WithTitle("TeamNexus API");
        options.WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient);
    });
}

app.UseCors("WebFrontend");

// Auth: JWT bearer (access token from HttpOnly cookie) + authorization policies.
app.UseAuthentication();
app.UseAuthorization();

// ---- Health check ----------------------------------------------------
var api = app.MapGroup("/api");
api.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    service = "TeamNexus.Api",
    time = DateTimeOffset.UtcNow,
}));

// ---- Feature module endpoints -----------------------------------------
app.MapAuthModuleEndpoints();
app.MapBoardModuleEndpoints();
app.MapBoardHub();

app.Run();
