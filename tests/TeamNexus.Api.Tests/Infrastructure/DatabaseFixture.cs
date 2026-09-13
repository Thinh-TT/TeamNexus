using System.Globalization;
using System.Net.Sockets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TeamNexus.Persistence.Data;

namespace TeamNexus.Api.Tests.Infrastructure;

/// <summary>
/// Owns the test database lifecycle for one test class (Phase 8 D2/D3).
/// <para>
/// <b>Why a real PostgreSQL and not EF InMemory/SQLite:</b> the schema depends on <c>jsonb</c>,
/// <c>bytea</c>, <c>pg_try_advisory_lock</c>, CHECK constraints, partial unique indexes and global
/// query filters. An in-memory provider would happily accept tests that pass while the real database
/// rejects the exact same statements.
/// </para>
/// <para>
/// <b>Why the DbContext comes from the app's own container:</b> that guarantees the test sees the
/// identical model (conventions, query filters, value converters) the application runs. A
/// hand-built <c>DbContextOptionsBuilder</c> can silently diverge, and then EF either reports
/// "pending model changes" or — worse — migrates a different schema than production uses.
/// </para>
/// <para>
/// <b>One host per process:</b> the <see cref="TeamNexusApiFactory"/> is created lazily, reused by
/// every test class (which also lets the ASP.NET Core server reuse its port/handler), and disposed
/// only at process exit. Per-test isolation comes from <see cref="CreateScenarioAsync"/>, which
/// truncates every table — not from rebuilding the host, which would dominate the runtime.
/// </para>
/// <para>
/// <b>No PostgreSQL reachable ⇒ the class skips, it does not fail (D4).</b> A machine without a
/// database stays useful for the priority-1 pure suites, while CI — which always has the
/// <c>postgres:18</c> service — fails loudly if anything is really broken.
/// </para>
/// </summary>
public sealed class DatabaseFixture : IAsyncLifetime
{
    /// <summary>Sentinel connection string; only used when <c>TEAMNEXUS_TEST_DB</c> is not set.</summary>
    public const string DefaultConnectionString =
        "Host=localhost;Port=5432;Database=TeamNexus_Test;Username=postgres;Password=postgres";

    /// <summary>PostgreSQL error code for "database does not exist".</summary>
    private const string UndefinedDatabase = "3D000";

    /// <summary>PostgreSQL error code for "database already exists".</summary>
    private const string DuplicateDatabase = "42P04";

    /// <summary>PostgreSQL error code for <c>unique_violation</c> — what a concurrent CREATE DATABASE raises.</summary>
    private const string DuplicateObject = "23505";

    /// <summary>How long to keep retrying the very first connection (service container first boot).</summary>
    private static readonly TimeSpan ServerReadyTimeout = TimeSpan.FromSeconds(60);

    private static readonly TimeSpan ServerReadyDelay = TimeSpan.FromSeconds(2);

    private static readonly int ServerReadyAttempts =
        Math.Max(1, (int)(ServerReadyTimeout / ServerReadyDelay));

    /// <summary>Stable key for the cross-process migration advisory lock (distinct from Phase 7's per-task locks).</summary>
    private const long MigrationLockKey = 0x5445_5354_4D49_4700L & long.MaxValue;

    private static readonly object Gate = new();

    /// <summary>
    /// Serializes the DB-backed suites across ALL test classes.
    /// <para>
    /// Every scenario truncates the whole schema, and every test class gets its own
    /// <see cref="DatabaseFixture"/> instance — so without a process-wide lock, two classes running
    /// in parallel would (a) see each other's rows and (b) deadlock inside
    /// <c>TRUNCATE … CASCADE</c>, which takes ACCESS EXCLUSIVE locks. The lock is held for the
    /// lifetime of the scenario and released when it is disposed, i.e. one DB test at a time.
    /// </para>
    /// </summary>
    private static readonly SemaphoreSlim DatabaseLock = new(1, 1);

    private static bool _databaseReady;

    private static bool _migrationsApplied;

    private static bool _availabilityReported;

    /// <summary>True when a PostgreSQL that can host the test schema is reachable.</summary>
    public bool IsAvailable { get; private set; }

    /// <summary>Why the database is unavailable — surfaced verbatim in the skip message.</summary>
    public string UnavailableReason { get; private set; } = "Chưa kiểm tra.";

    /// <summary>The connection string the suite is running against.</summary>
    public string ConnectionString { get; private set; } = ResolveConnectionString();

    /// <summary>
    /// Called once per test class: waits for PostgreSQL, creates the test database if needed, applies
    /// the migration chain, and makes sure the shared host exists. Never throws for an unreachable
    /// database — that is a skip, decided here and reported by <see cref="Require"/>.
    /// </summary>
    public async ValueTask InitializeAsync()
    {
        try
        {
            await WaitForServerAsync();
            EnsureDatabaseCreated();
            EnsureFactory();
            await EnsureMigratedAsync();

            IsAvailable = true;
        }
        catch (Exception ex)
        {
            IsAvailable = false;
            UnavailableReason = Describe(ex);
        }
    }

    /// <summary>Called at the top of every DB-backed test: skips (never fails) without a database.</summary>
    public void Require()
    {
        // One line in the job log is enough to tell "no DB ⇒ everything skipped" apart from
        // "everything ran" — without it, a fully-skipped run looks identical to a passing one.
        if (!_availabilityReported)
        {
            _availabilityReported = true;
            Console.WriteLine(
                IsAvailable
                    ? $"[Phase 8] Test DB ready: {ConnectionString}"
                    : $"[Phase 8] Test DB UNAVAILABLE — DB-backed tests will SKIP. Lý do: {UnavailableReason}");
        }

        if (!IsAvailable)
        {
            Assert.Skip(
                "PostgreSQL cho test không kết nối được → bỏ qua nhóm test cần DB (Phase 8 D4)."
                + Environment.NewLine
                + $"Chuỗi kết nối: {ConnectionString}"
                + Environment.NewLine
                + $"Lý do: {UnavailableReason}"
                + Environment.NewLine
                + "Cách chạy: `docker run --name teamnexus-pg -e POSTGRES_PASSWORD=postgres -p 5432:5432 -d postgres:18`"
                + " rồi trỏ TEAMNEXUS_TEST_DB vào đó (ví dụ "
                + "`Host=localhost;Port=5432;Database=TeamNexus_Test;Username=postgres;Password=postgres`).");
        }
    }

    /// <summary>Wipes every data table and returns a scenario bound to a clean database.</summary>
    public async Task<TestScenario> CreateScenarioAsync(ScriptedAiProvider? scriptedAi = null)
    {
        Require();

        // Held until the returned scenario is disposed: see DatabaseLock's remarks.
        await DatabaseLock.WaitAsync();

        try
        {
            var scenario = new TestScenario(
                Factory,
                releaseDatabaseLock: () => DatabaseLock.Release(),
                scriptedAiProvider: scriptedAi);

            await scenario.ResetDatabaseAsync();
            return scenario;
        }
        catch
        {
            DatabaseLock.Release();
            throw;
        }
    }

    /// <summary>
    /// A scenario whose host answers AI calls with <paramref name="scripted"/> instead of the
    /// production <c>FakeAiProvider</c>. Because the provider lives in DI, this scenario needs its
    /// own host — so it is deliberately a separate method (never the shared one).
    /// </summary>
    public async Task<TestScenario> CreateScriptedAiScenarioAsync(ScriptedAiProvider scripted)
    {
        Require();

        await DatabaseLock.WaitAsync();

        try
        {
            var factory = new TeamNexusApiFactory
            {
                ConnectionString = ConnectionString,
                ScriptedAi = scripted,
            };

            var scenario = new TestScenario(
                factory,
                releaseDatabaseLock: () =>
                {
                    DatabaseLock.Release();
                    factory.Dispose();
                },
                scriptedAiProvider: scripted);

            await scenario.ResetDatabaseAsync();
            return scenario;
        }
        catch
        {
            DatabaseLock.Release();
            throw;
        }
    }

    /// <summary>The shared host every scenario talks to (also used for the raw-SQL cleanup).</summary>
    public TeamNexusApiFactory Factory => TeamNexusApiFactory.Shared;

    /// <summary>
    /// A <see cref="TeamNexusDbContext"/> resolved from the application's scope — exactly the model
    /// production uses.
    /// </summary>
    public TeamNexusDbContext NewDbContext()
        => Factory.Services.CreateScope().ServiceProvider.GetRequiredService<TeamNexusDbContext>();

    /// <summary>The fixture instance is created per class; the host it shares outlives it.</summary>
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static void EnsureFactory()
    {
        // The shared host is created lazily by the factory itself so scenarios created directly
        // (without a fixture) still address the same server instance.
        _ = TeamNexusApiFactory.Shared;
    }

    /// <summary>
    /// Applies the migration chain once per process. Safe under xUnit's parallel collections: a
    /// session-level advisory lock guards the first caller, and every other caller <b>waits until the
    /// schema actually exists</b> before returning.
    /// <para>
    /// That wait is not optional. An earlier version let the loser of the lock return immediately and
    /// just remember "migrations are done"; it then truncated tables that did not exist yet, the
    /// resulting exception flipped <c>IsAvailable</c> to false, and — because the availability
    /// decision is cached for the process — every DB-backed test in that class silently became a
    /// skip. On CI that hid 61 of 172 tests behind a green job.
    /// </para>
    /// </summary>
    private async Task EnsureMigratedAsync()
    {
        lock (Gate)
        {
            if (_migrationsApplied)
            {
                return;
            }
        }

        // The database itself is ensured HERE, not only in InitializeAsync, so this method is safe
        // even when the database was dropped between two test classes.
        EnsureDatabaseCreated();

        try
        {
            await MigrateOnceAsync();
        }
        catch (PostgresException ex) when (ex.SqlState == UndefinedDatabase)
        {
            // The database vanished after `_databaseReady` was cached (a developer wiped it, or a
            // previous process dropped it). Forget the cached decisions and rebuild from scratch
            // instead of permanently downgrading the whole class to "skipped".
            lock (Gate)
            {
                _databaseReady = false;
                _migrationsApplied = false;
            }

            EnsureDatabaseCreated();
            await MigrateOnceAsync();
        }

        MarkMigrated();
    }

    /// <summary>
    /// One migration attempt. When another process already holds the advisory lock this <b>waits until
    /// the schema is really there</b> before returning.
    /// <para>
    /// That wait is the whole point. Returning as soon as the lock is taken flips <c>_migrationsApplied</c>
    /// to true while the winner is still creating tables, so this class then runs
    /// <c>TRUNCATE …</c> against a half-migrated schema and dies with
    /// <c>42P01: relation "task_attachments" does not exist</c> — observed on a fresh database before
    /// this was fixed.
    /// </para>
    /// </summary>
    private async Task MigrateOnceAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        var acquired = false;

        await using (var acquire = new NpgsqlCommand("select pg_try_advisory_lock(@key)", connection))
        {
            acquire.Parameters.AddWithValue("key", MigrationLockKey);
            acquired = (bool)(await acquire.ExecuteScalarAsync() ?? false);
        }

        if (!acquired)
        {
            // Someone else is migrating: block until the history table is actually usable.
            await WaitForMigrationsAsync();
            return;
        }

        try
        {
            await using var db = NewDbContext();
            await db.Database.MigrateAsync();
        }
        finally
        {
            await using var release = new NpgsqlCommand("select pg_advisory_unlock(@key)", connection);
            release.Parameters.AddWithValue("key", MigrationLockKey);
            await release.ExecuteNonQueryAsync();
        }
    }

    private static void MarkMigrated()
    {
        lock (Gate)
        {
            _migrationsApplied = true;
        }
    }

    /// <summary>
    /// Waits until the server accepts connections, for up to <see cref="ServerReadyTimeout"/>.
    /// <para>
    /// Needed because on a fresh CI runner the PostgreSQL service container can still be doing its
    /// first-time initdb while the test process starts. The availability decision is cached for the
    /// process, so a single early failure would silently turn every DB-backed test in this class into
    /// a skip — the suite would look green while having actually verified nothing. Retrying removes
    /// that failure mode entirely.
    /// </para>
    /// </summary>
    private async Task WaitForServerAsync()
    {
        Exception? lastError = null;

        for (var attempt = 0; attempt < ServerReadyAttempts; attempt++)
        {
            try
            {
                await using var connection = new NpgsqlConnection(BuildAdminConnectionString());
                await connection.OpenAsync();
                return;
            }
            catch (Exception ex)
            {
                lastError = ex;

                if (attempt < ServerReadyAttempts - 1)
                {
                    await Task.Delay(ServerReadyDelay);
                }
            }
        }

        throw new InvalidOperationException(
            $"PostgreSQL không nhận kết nối sau {ServerReadyTimeout.TotalSeconds:N0}s. {Describe(lastError!)}",
            lastError);
    }

    /// <summary>Creates the test database on first use (CREATE DATABASE cannot run in a transaction).</summary>
    private void EnsureDatabaseCreated()
    {
        // The whole check-and-create runs under the lock. Doing the wait outside it (as an earlier
        // version did) let two test classes both observe "not created yet" and both issue
        // CREATE DATABASE — which fails with 23505 on the pg_database unique index, not with the
        // 42P04 the catch below used to expect.
        lock (Gate)
        {
            if (_databaseReady)
            {
                return;
            }

            using var connection = new NpgsqlConnection(BuildAdminConnectionString());
            connection.Open();

            var database = new NpgsqlConnectionStringBuilder(ConnectionString).Database;
            using var create = new NpgsqlCommand($"CREATE DATABASE \"{database}\"", connection);

            try
            {
                create.ExecuteNonQuery();
            }
            catch (PostgresException ex)
                when (ex.SqlState is DuplicateDatabase or DuplicateObject)
            {
                // Already there — the normal case after the first run. 42P04 = duplicate_database;
                // 23505 = unique_violation, which is what a concurrent CREATE DATABASE produces on
                // the pg_database index (observed on a fresh CI runner before this was locked).
            }

            _databaseReady = true;
        }
    }

    private async Task<bool> HasAppliedMigrationsAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            select exists (
                select 1 from information_schema.tables
                where table_schema = 'public' and table_name = '__EFMigrationsHistory')
            """,
            connection);

        if (!(bool)(await command.ExecuteScalarAsync() ?? false))
        {
            return false;
        }

        await using var count = new NpgsqlCommand(
            "select count(*) from \"__EFMigrationsHistory\"",
            connection);

        var applied = Convert.ToInt64(await count.ExecuteScalarAsync() ?? 0L, CultureInfo.InvariantCulture);
        return applied > 0;
    }

    private async Task WaitForMigrationsAsync()
    {
        for (var attempt = 0; attempt < 120; attempt++)
        {
            await Task.Delay(500);

            try
            {
                if (await HasAppliedMigrationsAsync())
                {
                    return;
                }
            }
            catch (PostgresException ex) when (ex.SqlState == UndefinedDatabase)
            {
                // Another process is still creating the database.
            }
        }

        throw new InvalidOperationException(
            "Hết thời gian chờ một process khác chạy migration cho database test.");
    }

    /// <summary>Same server, always-present <c>postgres</c> database, so the test one can be created.</summary>
    private string BuildAdminConnectionString()
    {
        var builder = new NpgsqlConnectionStringBuilder(ConnectionString);

        if (string.Equals(builder.Database, "postgres", StringComparison.OrdinalIgnoreCase))
        {
            return ConnectionString;
        }

        builder.Database = "postgres";
        return builder.ConnectionString;
    }

    private static string ResolveConnectionString()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable("TEAMNEXUS_TEST_DB");

        return string.IsNullOrWhiteSpace(fromEnvironment)
            ? DefaultConnectionString
            : fromEnvironment.Trim();
    }

    private static string Describe(Exception ex) => ex switch
    {
        PostgresException pg => $"PostgreSQL trả lỗi {pg.SqlState}: {pg.MessageText}",
        NpgsqlException npg => $"NpgsqlException: {npg.Message}",
        SocketException sock => $"SocketException: {sock.Message}",
        _ => $"{ex.GetType().Name}: {ex.Message}",
    };
}
