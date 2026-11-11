using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using TeamNexus.Modules.Ai;
using TeamNexus.Modules.Auth;
using TeamNexus.Modules.Auth.Endpoints;
using TeamNexus.Modules.Board;
using TeamNexus.Modules.Board.Endpoints;
using TeamNexus.Modules.Board.Hubs;
using TeamNexus.Modules.Reporting;

var builder = WebApplication.CreateBuilder(args);

// =====================================================================
// Services (DI)
// =====================================================================
builder.Services.AddProblemDetails();

// Forwarded headers from reverse proxies (Render, Cloudflare)
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

// OpenAPI document (.NET 10 built-in). UI served by Scalar at /scalar.
builder.Services.AddOpenApi();

// ---- Module registrations (Modular Monolith) -------------------------
// Each module exposes one extension method, e.g. builder.Services.AddAuthModule(...).
// Auth internals (DbContext, Identity, OAuth, JWT) arrive in Phase 1 §2–§3.
builder.Services.AddAuthModule(builder.Configuration);

// Board module: Kanban CRUD services + SignalR hub registration (Phase 2 §2–§3).
builder.Services.AddBoardModule(builder.Configuration);

// Ai module: DeepSeek config + DI wiring for AI Smart Setup (Phase 3 §1).
builder.Services.AddAiModule(builder.Configuration);

// Reporting module: báo cáo tiến độ/hiệu suất + xuất PDF/Excel on-demand (Phase 6).
// Đăng ký SAU Board/Ai để không đổi thứ tự resolve của 2 module cũ (bất biến ở phase-5 §2).
builder.Services.AddReportingModule(builder.Configuration);

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
              .AllowCredentials()
              .WithExposedHeaders("X-XSRF-TOKEN");
    });
});

// ---- Data Protection key ring (Phase 15.3 §T15.3.1) -------------------
// GitHub webhook secrets are encrypted at rest via IDataProtectionProvider. The default key ring lives
// in the machine profile, which is ephemeral inside a container: every redeploy would leave previously
// stored secrets undecryptable and every GitHub delivery would answer 401. Phase 15.3 design §13.1
// therefore requires an explicitly durable ring, configured here through environment variables:
//   Integrations__DataProtection__KeysPath      → mounted directory (render/vps volume)
//   Integrations__DataProtection__CertPath      → PFX (vps), with CertPassword
var dataProtection = builder.Services.AddDataProtection().SetApplicationName("TeamNexus");
var dataProtectionKeysPath = builder.Configuration["Integrations:DataProtection:KeysPath"];
var dataProtectionCertPath = builder.Configuration["Integrations:DataProtection:CertPath"];

if (!string.IsNullOrWhiteSpace(dataProtectionCertPath))
{
    var dataProtectionCertPassword = builder.Configuration["Integrations:DataProtection:CertPassword"];
    dataProtection.ProtectKeysWithCertificate(
        X509CertificateLoader.LoadPkcs12FromFile(dataProtectionCertPath, dataProtectionCertPassword));
}
else if (!string.IsNullOrWhiteSpace(dataProtectionKeysPath))
{
    Directory.CreateDirectory(dataProtectionKeysPath);
    dataProtection.PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath));
}

var app = builder.Build();

if (string.IsNullOrWhiteSpace(dataProtectionKeysPath) && string.IsNullOrWhiteSpace(dataProtectionCertPath))
{
    app.Logger.LogWarning(
        "DataProtection đang dùng key ring mặc định của máy. Trong container, secret tích hợp đã mã hoá sẽ "
        + "KHÔNG giải mã được sau redeploy (GitHub delivery trả 401). Cấu hình "
        + "Integrations:DataProtection:KeysPath (thư mục bền) hoặc CertPath (PFX) trước khi bật tích hợp trên production.");
}

// =====================================================================
// Middleware pipeline
// =====================================================================
app.UseForwardedHeaders();

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

// ---- Health ----------------------------------------------------------
// Workspace endpoints (list/detail/rename/ownership/delete/activity) moved into the Board module
// in Phase 10 §2 — see TeamNexus.Modules.Board/Endpoints/WorkspacesEndpoints.cs.
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
app.MapAiModuleEndpoints();
app.MapReportingModuleEndpoints();
app.MapBoardHub();

app.Run();

/// <summary>
/// Marker cho test/harness: <c>WebApplicationFactory&lt;Program&gt;</c> cần một type <c>public</c> của entry
/// point (top-level statements sinh ra type <c>Program</c> là <c>internal</c>). Chỉ là khai báo partial
/// rỗng — không thêm hành vi nào vào pipeline (Phase 6 §5, quyết định D29).
/// </summary>
public partial class Program
{
}
