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

// OpenAPI document (.NET 10 built-in). UI served by Scalar at /scalar.
builder.Services.AddOpenApi();

// ---- Module registrations (Modular Monolith) -------------------------
// Each module exposes one extension method, e.g. builder.Services.AddAuthModule(...).
// Auth internals (DbContext, Identity, OAuth, JWT) arrive in Phase 1 §2–§3.
builder.Services.AddAuthModule(builder.Configuration);

// Board module: Kanban CRUD services + SignalR hub registration (Phase 2 §2–§3).
builder.Services.AddBoardModule();

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

// ---- Health & Workspace endpoints -------------------------------------
var api = app.MapGroup("/api");
api.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    service = "TeamNexus.Api",
    time = DateTimeOffset.UtcNow,
}));

api.MapGet("/workspaces", async (TeamNexus.Persistence.Data.TeamNexusDbContext db, HttpContext http, CancellationToken ct) =>
{
    var userIdClaim = http.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    if (!Guid.TryParse(userIdClaim, out var userId))
    {
        return Results.Unauthorized();
    }

    var memberships = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(
        db.WorkspaceMembers
            .Include(wm => wm.Workspace)
            .Where(wm => wm.UserId == userId && wm.Workspace != null),
        ct);

    if (memberships.Count == 0)
    {
        var defaultWs = new TeamNexus.Persistence.Data.Entities.Workspace
        {
            Id = Guid.NewGuid(),
            Name = "Không Gian Làm Việc Chính",
            Description = "Workspace mặc định để quản lý bảng Kanban",
            OwnerId = userId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        var defaultMember = new TeamNexus.Persistence.Data.Entities.WorkspaceMember
        {
            WorkspaceId = defaultWs.Id,
            UserId = userId,
            Role = TeamNexus.Persistence.Data.Entities.WorkspaceRole.Admin,
            JoinedAt = DateTimeOffset.UtcNow,
            Workspace = defaultWs,
        };

        db.Workspaces.Add(defaultWs);
        db.WorkspaceMembers.Add(defaultMember);
        await db.SaveChangesAsync(ct);
        memberships.Add(defaultMember);
    }

    return Results.Ok(memberships.Select(wm => new
    {
        id = wm.WorkspaceId,
        name = wm.Workspace?.Name ?? "Workspace",
        description = wm.Workspace?.Description,
        role = wm.Role.ToString(),
    }));
}).RequireAuthorization();

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
