using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TeamNexus.Modules.Ai.Services;
using TeamNexus.Modules.Auth;
using TeamNexus.Modules.Auth.Services;
using TeamNexus.Persistence.Data;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Api.Tests.Infrastructure;

/// <summary>
/// One isolated integration-test scenario: a clean database plus ready-to-use
/// <see cref="HttpClient"/>s pointed at the in-process API.
/// <para>
/// <b>Two authentication paths, on purpose:</b>
/// <list type="bullet">
///   <item><see cref="AsUserAsync"/> / <see cref="WithTamperedTokenAsync"/> present the access token
///   in the <c>Authorization: Bearer</c> header. The production app accepts it there as well as in
///   the HttpOnly cookie, and a header is what makes multi-user assertions (A vs B) possible without
///   cookie jars fighting each other.</item>
///   <item><see cref="SignedInAsync"/> drives the real cookie + antiforgery contract
///   (<c>/api/auth/antiforgery</c> → <c>X-XSRF-TOKEN</c> round-trip), which is what the
///   refresh-token rotation, logout and CSRF suites need.</item>
/// </list>
/// </para>
/// <para>Usage: <c>await using var scenario = await TestScenario.CreateAsync();</c></para>
/// </summary>
public sealed class TestScenario : IAsyncDisposable
{
    /// <summary>Base address understood by the in-process test server.</summary>
    public static readonly Uri BaseAddress = new("http://localhost/");

    /// <summary>
    /// The connection string the suites run against — exposed for the few tests that need to build
    /// their own host (the disabled-feature 503 checks and the reaper's restart).
    /// </summary>
    public static string ConnectionStringForTests => TeamNexusApiFactory.Shared.ConnectionString;

    private readonly WebApplicationFactory<Program> _factory;
    private readonly Action? _releaseDatabaseLock;
    private readonly ScriptedAiProvider? _scriptedAiProvider;
    private CookieContainer _cookieJar = new();
    private string? _xsrfToken;

    /// <summary>A scenario always belongs to a migrated database owned by a <see cref="DatabaseFixture"/>.</summary>
    internal TestScenario(
        WebApplicationFactory<Program> factory,
        Action? releaseDatabaseLock = null,
        ScriptedAiProvider? scriptedAiProvider = null)
    {
        _factory = factory;
        _releaseDatabaseLock = releaseDatabaseLock;
        _scriptedAiProvider = scriptedAiProvider;
    }

    /// <summary>Services from the running host (resolve the real <c>AuthService</c>, providers, …).</summary>
    public IServiceProvider Services => _factory.Services;

    /// <summary>The scripted provider installed by <c>WithScriptedAiAsync</c>, when present.</summary>
    public ScriptedAiProvider? ScriptedAi => _scriptedAiProvider;

    /// <summary>
    /// The production <c>FakeAiProvider</c> — used by the suites that must exercise the real offline
    /// flow (Smart Setup proposal shape, Observer payload, Agent tool-calling script).
    /// </summary>
    public IAiProvider ProductionFakeProvider => _factory.Services.GetRequiredService<IAiProvider>();

    /// <summary>
    /// A <see cref="TeamNexusDbContext"/> built from the application's own DI options, so the test
    /// always sees exactly the model (conventions, filters, converters) production uses.
    /// The context is scoped, so it is resolved from a fresh scope; the caller owns disposal and the
    /// scope lives only as long as the request-like unit of work.
    /// </summary>
    public TeamNexusDbContext NewDbContext()
    {
        var scope = _factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<TeamNexusDbContext>();
    }

    /// <summary>Runs <paramref name="action"/> against a fresh context.</summary>
    public async Task WithDbContextAsync(Func<TeamNexusDbContext, Task> action)
    {
        await using var db = NewDbContext();
        await action(db);
    }

    /// <summary>
    /// Wipes every data table (TRUNCATE … CASCADE) and clears the local session.
    /// <para>
    /// Only tables that actually exist are truncated. A single missing table makes the whole
    /// <c>TRUNCATE</c> statement fail with <c>42P01</c>, which turns a schema problem — or simply a
    /// database that another process is still migrating — into confusing test failures. Filtering
    /// keeps this reset honest and self-describing instead.
    /// </para>
    /// </summary>
    internal async Task ResetDatabaseAsync()
    {
        await using var db = NewDbContext();

        var existing = await GetExistingTableNamesAsync(db);

        // `roles` is deliberately absent: it is seeded once by InitialSchema and never rewritten.
        string[] wanted =
        [
            "task_attachments", "agent_runs", "task_comments", "notifications", "activity_logs",
            "ai_observer_runs", "ai_action_logs", "task_labels", "tasks", "board_columns", "boards",
            "labels", "workspace_members", "refresh_tokens", "user_roles", "user_claims",
            "user_tokens", "user_logins", "users", "workspaces",
        ];

        var present = wanted.Where(existing.Contains).ToList();

        if (present.Count == 0)
        {
            throw new InvalidOperationException(
                "Không tìm thấy bảng nào của TeamNexus trong database test — migration chưa chạy xong?");
        }

        // TRUNCATE … CASCADE rather than ordered DELETEs: every relationship in this schema is
        // ON DELETE RESTRICT, so a hand-maintained deletion order would break the day a new FK is added.
        // Identifiers are quoted and come from the hard-coded allow-list above (never from user input),
        // so the statement is built as a plain string rather than an interpolated one.
        var tableList = string.Join(", ", present.Select(name => "\"" + name + "\""));
        var sql = "TRUNCATE TABLE " + tableList + " RESTART IDENTITY CASCADE;";

        await db.Database.ExecuteSqlRawAsync(sql);

        db.ChangeTracker.Clear();
        ResetSession();
    }

    private static async Task<HashSet<string>> GetExistingTableNamesAsync(TeamNexusDbContext db)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        // The connection belongs to the DbContext: closing/disposing it here would leave EF with a
        // disposed connection for the statements that follow (observed as ObjectDisposedException on
        // the TRUNCATE). So it is leaned on without taking ownership.
        var connection = db.Database.GetDbConnection();
        var wasClosed = connection.State != System.Data.ConnectionState.Open;

        if (wasClosed)
        {
            await connection.OpenAsync();
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public'";

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                names.Add(reader.GetString(0));
            }
        }
        finally
        {
            if (wasClosed)
            {
                await connection.CloseAsync();
            }
        }

        return names;
    }

    // ---- seeding ------------------------------------------------------------

    /// <summary>Inserts a user + its global Identity role, exactly as a login would leave it.</summary>
    public async Task<ApplicationUser> CreateUserAsync(
        string displayName,
        string? email = null,
        string role = AuthConstants.DefaultMemberRole)
    {
        await using var db = NewDbContext();

        var address = email ?? $"{Guid.NewGuid():N}@example.test";

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = address,
            NormalizedUserName = address.ToUpperInvariant(),
            Email = address,
            NormalizedEmail = address.ToUpperInvariant(),
            EmailConfirmed = true,
            DisplayName = displayName,
            LockoutEnabled = true,
            SecurityStamp = Guid.NewGuid().ToString("D"),
            ConcurrencyStamp = Guid.NewGuid().ToString("D"),
        };

        db.Users.Add(user);
        db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = user.Id, RoleId = RoleId(role) });

        await db.SaveChangesAsync();
        return user;
    }

    /// <summary>Creates a workspace with <paramref name="owner"/> as Admin and the rest as members.</summary>
    public async Task<Workspace> CreateWorkspaceAsync(
        ApplicationUser owner,
        string name = "Workspace Test",
        params (ApplicationUser User, WorkspaceRole Role)[] others)
    {
        await using var db = NewDbContext();

        var workspace = new Workspace
        {
            Id = Guid.NewGuid(),
            Name = name,
            OwnerId = owner.Id,
        };

        db.Workspaces.Add(workspace);
        db.WorkspaceMembers.Add(new WorkspaceMember
        {
            WorkspaceId = workspace.Id,
            UserId = owner.Id,
            Role = WorkspaceRole.Admin,
        });

        foreach (var (user, role) in others)
        {
            db.WorkspaceMembers.Add(new WorkspaceMember
            {
                WorkspaceId = workspace.Id,
                UserId = user.Id,
                Role = role,
            });
        }

        await db.SaveChangesAsync();
        return workspace;
    }

    /// <summary>Adds a board with two columns ("Todo", "Done") to a workspace.</summary>
    public async Task<(Board Board, BoardColumn Todo, BoardColumn Done)> CreateBoardAsync(
        Workspace workspace,
        string name = "Board Test")
    {
        await using var db = NewDbContext();

        var board = new Board
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspace.Id,
            Name = name,
        };

        var todo = new BoardColumn
        {
            Id = Guid.NewGuid(),
            BoardId = board.Id,
            Name = "Todo",
            Position = 0,
            IsDone = false,
        };

        var done = new BoardColumn
        {
            Id = Guid.NewGuid(),
            BoardId = board.Id,
            Name = "Done",
            Position = 1,
            IsDone = true,
        };

        db.Boards.Add(board);
        db.BoardColumns.AddRange(todo, done);
        await db.SaveChangesAsync();

        return (board, todo, done);
    }

    /// <summary>Adds a task directly, so a suite can set up a precise starting state.</summary>
    public async Task<BoardTask> CreateTaskAsync(
        Board board,
        BoardColumn column,
        ApplicationUser createdBy,
        string title = "Task Test",
        Guid? assigneeId = null,
        DateTimeOffset? dueDate = null)
    {
        await using var db = NewDbContext();

        var task = new BoardTask
        {
            Id = Guid.NewGuid(),
            BoardId = board.Id,
            ColumnId = column.Id,
            Title = title,
            Position = 0,
            CreatedBy = createdBy.Id,
            AssigneeId = assigneeId,
            DueDate = dueDate,
        };

        db.Tasks.Add(task);
        await db.SaveChangesAsync();
        return task;
    }

    /// <summary>Row count for a table — used by "nothing must have been written" assertions.</summary>
    public async Task<int> CountAsync<TEntity>()
        where TEntity : class
    {
        await using var db = NewDbContext();
        return await db.Set<TEntity>().CountAsync();
    }

    /// <summary>Row count under a filter, for the same purpose.</summary>
    public async Task<int> CountAsync<TEntity>(System.Linq.Expressions.Expression<Func<TEntity, bool>> predicate)
        where TEntity : class
    {
        await using var db = NewDbContext();
        return await db.Set<TEntity>().CountAsync(predicate);
    }

    /// <summary>Reads one entity straight from the database through a fresh context.</summary>
    public async Task<TEntity?> FindAsync<TEntity>(Guid id)
        where TEntity : class
    {
        await using var db = NewDbContext();
        return await db.Set<TEntity>().FindAsync(id);
    }

    /// <summary>
    /// Reads one entity while bypassing the global query filters — required to observe soft-deleted
    /// rows (e.g. <c>tasks.deleted_at</c>), which are invisible to every normal query by design.
    /// </summary>
    public async Task<TEntity?> FindIncludingSoftDeletedAsync<TEntity>(Guid id)
        where TEntity : class
    {
        await using var db = NewDbContext();
        return await db.Set<TEntity>().IgnoreQueryFilters().FirstOrDefaultAsync(e => EF.Property<Guid>(e, "Id") == id);
    }

    // ---- HTTP clients -------------------------------------------------------

    /// <summary>An unauthenticated client (with a working antiforgery pair).</summary>
    public async Task<TestHttpClient> AnonymousAsync()
    {
        var client = Wrap(NewClient());
        await EnsureAntiforgeryAsync(client);
        return client;
    }

    /// <summary>
    /// A client authenticated with the same claims a real access token carries.
    /// <para>
    /// The access token cookie is set into the session jar (rather than an <c>Authorization</c>
    /// header) so a single client can carry BOTH the token and the antiforgery cookie — which is the
    /// shape the browser actually has, and therefore the shape the CSRF filter validates. Use
    /// <see cref="WithTamperedToken"/> / <see cref="WithExpiredToken"/> for the header-only 401 paths.
    /// </para>
    /// </summary>
    public async Task<TestHttpClient> AsUserAsync(ApplicationUser user, string? role = null)
    {
        SetCookie(AuthConstants.AccessTokenCookie, TestJwt.Create(user, role));

        var client = Wrap(NewClient());
        await EnsureAntiforgeryAsync(client);
        return client;
    }

    /// <summary>A client presenting a token signed with the wrong key (401 assertions).</summary>
    public TestHttpClient WithTamperedToken(ApplicationUser user)
    {
        var client = NewClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestJwt.Create(user, role: null, TeamNexusApiFactory.WrongSigningKey));

        return Wrap(client);
    }

    /// <summary>A client presenting an already-expired token (lifetime assertions).</summary>
    public TestHttpClient WithExpiredToken(ApplicationUser user)
    {
        var client = NewClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwt.CreateExpired(user));

        return Wrap(client);
    }

    /// <summary>
    /// The cookie path: a real login session as <paramref name="user"/> — HttpOnly access + refresh
    /// cookies plus a working antiforgery pair, exactly what the SPA has after OAuth.
    /// </summary>
    public async Task<TestHttpClient> SignedInAsync(ApplicationUser user)
    {
        var pair = await IssueTokenPairAsync(user);
        return await SignedInAsync(pair);
    }

    /// <summary>Same, for an already issued <paramref name="pair"/>.</summary>
    public async Task<TestHttpClient> SignedInAsync(AuthService.TokenPair pair)
    {
        // Only the antiforgery token is dropped — the cookie jar is the caller's session, and
        // clearing it here would throw away cookies a test deliberately planted.
        _xsrfToken = null;

        // Cookies first, THEN the antiforgery round-trip: ASP.NET Core binds the token to the current
        // claims-based user, so a token minted while anonymous is rejected (403) once the request
        // carries the access cookie. httpClient.ts handles this in production with the same
        // "fetch a fresh token on a CSRF 403" retry.
        SetCookie(AuthConstants.AccessTokenCookie, pair.AccessToken);
        SetCookie(AuthConstants.RefreshTokenCookie, pair.RawRefreshToken);

        var client = Wrap(NewClient());
        await EnsureAntiforgeryAsync(client);
        return client;
    }

    /// <summary>Drops the whole session (cookies + antiforgery token) — "fresh browser profile".</summary>
    public void ResetSession()
    {
        _cookieJar = new CookieContainer();
        _xsrfToken = null;
    }

    /// <summary>
    /// Fetches <c>/api/auth/antiforgery</c> and keeps the token so CSRF-protected verbs can be sent
    /// with the matching <c>X-XSRF-TOKEN</c> header, mirroring <c>httpClient.ts</c>.
    /// </summary>
    public async Task EnsureAntiforgeryAsync(TestHttpClient client)
    {
        var response = await client.Http.GetAsync("/api/auth/antiforgery");
        response.EnsureSuccessStatusCode();

        _xsrfToken = ReadSetCookieToken(response)
            ?? throw new InvalidOperationException(
                "GET /api/auth/antiforgery did not set the XSRF-TOKEN cookie; CSRF suites cannot run.");
    }

    /// <summary>Applies the current antiforgery token to a single outbound request.</summary>
    internal void ApplyAntiforgeryHeader(HttpRequestMessage request)
    {
        if (_xsrfToken is null)
        {
            throw new InvalidOperationException(
                "No antiforgery token yet — call AnonymousAsync/AsUserAsync/SignedInAsync first.");
        }

        request.Headers.Remove(AuthConstants.XsrfRequestHeader);
        request.Headers.Add(AuthConstants.XsrfRequestHeader, _xsrfToken);
    }

    /// <summary>Drops just the cached antiforgery token (rotation/logout invalidates it).</summary>
    public void ClearAntiforgeryToken() => _xsrfToken = null;

    // ---- auth helpers -------------------------------------------------------

    /// <summary>Issues a real access token + refresh-token pair through the app's own AuthService.</summary>
    public async Task<AuthService.TokenPair> IssueTokenPairAsync(ApplicationUser user)
    {
        await using var scope = Services.CreateAsyncScope();
        var authService = scope.ServiceProvider.GetRequiredService<AuthService>();
        return await authService.IssueTokenPairAsync(user);
    }

    /// <summary>Counts persisted refresh-token rows for a user (rotation assertions).</summary>
    public async Task<int> CountRefreshTokensAsync(Guid userId)
    {
        await using var db = NewDbContext();
        return await db.RefreshTokens.CountAsync(t => t.UserId == userId);
    }

    /// <summary>True when a refresh token with this raw value is already revoked.</summary>
    public async Task<bool> IsRefreshTokenRevokedAsync(string rawToken)
    {
        var hash = JwtService.HashToken(rawToken);

        await using var db = NewDbContext();
        var row = await db.RefreshTokens.SingleOrDefaultAsync(t => t.TokenHash == hash);

        return row?.RevokedAt is not null;
    }

    /// <summary>The persisted hash for a raw refresh token (proves only the hash is stored).</summary>
    public async Task<string?> FindRefreshTokenHashAsync(string rawToken)
    {
        var hash = JwtService.HashToken(rawToken);

        await using var db = NewDbContext();
        var row = await db.RefreshTokens.SingleOrDefaultAsync(t => t.TokenHash == hash);

        return row?.TokenHash;
    }

    /// <summary>Puts only a refresh cookie into the session jar (refresh/logout edge cases).</summary>
    public void AddRefreshCookie(string rawToken)
        => SetCookie(AuthConstants.RefreshTokenCookie, rawToken);

    /// <summary>
    /// Posts to a CSRF-protected endpoint with a freshly minted antiforgery pair.
    /// <para>
    /// The token is refetched on every call on purpose. Two things make a previously fetched token
    /// stale: rotation/logout replaces the access cookie, and ASP.NET Core binds the token to the
    /// claims-based user — so a token minted for the previous identity is rejected with 403. This is
    /// exactly the "fetch a fresh token on a CSRF 403" retry <c>httpClient.ts</c> implements.
    /// </para>
    /// </summary>
    public async Task<HttpResponseMessage> PostWithFreshAntiforgeryAsync(TestHttpClient client, string url)
    {
        await EnsureAntiforgeryAsync(client);

        var request = new HttpRequestMessage(HttpMethod.Post, url);
        return await client.SendAsync(request);
    }

    /// <summary>Convenience wrapper over <c>/api/auth/refresh</c>.</summary>
    public Task<HttpResponseMessage> RefreshAsync(TestHttpClient client)
        => PostWithFreshAntiforgeryAsync(client, "/api/auth/refresh");

    /// <summary>Convenience wrapper over <c>/api/auth/logout</c>.</summary>
    public Task<HttpResponseMessage> LogoutAsync(TestHttpClient client)
        => PostWithFreshAntiforgeryAsync(client, "/api/auth/logout");

    public async ValueTask DisposeAsync()
    {
        // The host is process-wide (DatabaseFixture owns its lifetime), so disposing a scenario only
        // ends its own session — and hands the database back to the next test.
        ResetSession();
        _releaseDatabaseLock?.Invoke();
        await ValueTask.CompletedTask;
    }

    // ---- internals ----------------------------------------------------------

    private HttpClient NewClient()
    {
        var client = _factory.CreateDefaultClient(new SessionCookieHandler(_cookieJar));

        // Every client is isolated: whatever the caller adds stays on that client.
        client.BaseAddress = BaseAddress;
        return client;
    }

    private TestHttpClient Wrap(HttpClient http) => new(http, this);

    /// <summary>Sets the cookie pair a real browser would receive, so cookie-based flows can be tested.</summary>
    public void SetCookie(string name, string value)
    {
        // Drop any previous value first: a CookieContainer would otherwise keep BOTH headers for the
        // same name and send "old; new", from which the server reads the stale one — exactly how an
        // antiforgery token silently goes stale after a login or a rotation.
        _cookieJar.Add(BaseAddress, new Cookie(name, string.Empty, "/", "localhost")
        {
            Expires = DateTime.UtcNow.AddDays(-1),
        });

        _cookieJar.Add(BaseAddress, new Cookie(name, value, "/", "localhost"));
    }

    private static string? ReadSetCookieToken(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var headers))
        {
            return null;
        }

        foreach (var header in headers)
        {
            var parts = header.Split(';', 2)[0];
            var equals = parts.IndexOf('=');

            if (equals > 0 && parts[..equals].Trim() == AuthConstants.XsrfTokenCookie)
            {
                return Uri.UnescapeDataString(parts[(equals + 1)..]);
            }
        }

        return null;
    }

    private static Guid RoleId(string role) => role switch
    {
        "Admin" => IdentityRoles.AdminId,
        "Manager" => IdentityRoles.ManagerId,
        "Member" => IdentityRoles.MemberId,
        _ => IdentityRoles.MemberId,
    };

    /// <summary>Typed GET helper kept next to the scenario for readability of the read suites.</summary>
    public static Task<T?> GetJsonAsync<T>(HttpClient client, string url)
        => client.GetFromJsonAsync<T>(url);
}
