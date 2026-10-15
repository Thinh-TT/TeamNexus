using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using TeamNexus.Api.Tests.Infrastructure;

namespace TeamNexus.Api.Tests.Integration;

public sealed class JevLiveMeasurementTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _database;

    public JevLiveMeasurementTests(DatabaseFixture database)
    {
        _database = database;
    }

    [Fact]
    public async Task MeasureLiveJevLatency_WithRealApiKey()
    {
        var apiKey = Environment.GetEnvironmentVariable("REAL_JEV_API_KEY")?.Trim();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            Console.WriteLine("Bỏ qua đo live Jev: biến môi trường REAL_JEV_API_KEY chưa được đặt.");
            return;
        }

        await using var scenario = await _database.CreateScenarioAsync(jevApiKey: apiKey);
        var admin = await scenario.CreateUserAsync("Quản trị Đo Lường");
        var workspace = await scenario.CreateWorkspaceAsync(admin);
        var (board, _, _) = await scenario.CreateBoardAsync(workspace, "Board Đo Latency");

        using var client = await scenario.AsUserAsync(admin);

        var sampleTasks = new[]
        {
            ("Tối ưu hóa query database PostgreSQL và index cho bảng tasks", "Cần phân tích slow query log và tạo index B-Tree."),
            ("Fix responsive layout trên màn hình mobile cho Kanban board", "Giao diện bị tràn ngang trên iPhone và Android."),
            ("Tích hợp authentication với OAuth2 Google và GitHub", "Thêm sign-in with Google vào trang login."),
            ("Viết unit test cho TaskService và BoardService", "Bổ sung unit test kiểm tra business validation."),
            ("Cấu hình CI pipeline GitHub Actions chạy automated test", "Workflow chạy build và test tự động khi push."),
            ("Refactor code modules tách biệt giữa Ai và Board", "Tuân thủ nguyên tắc quan hệ một chiều."),
            ("Kiểm tra lỗ hổng bảo mật SQL injection và XSS", "Rà soát toàn bộ endpoint và query filter."),
            ("Tạo trang quản lý thành viên và phân quyền trong workspace", "Thêm giao diện đổi role cho Manager."),
            ("Sửa lỗi mất kết nối SignalR khi mạng chập chờn", "Thêm cơ chế tự động reconnect và thông báo UI."),
            ("Tối ưu hóa bundle size frontend Vite React", "Code splitting và lazy load các modal nặng."),
        };

        Console.WriteLine("=== BẮT ĐẦU WARM-UP (3 lượt) ===");
        for (var w = 0; w < 3; w++)
        {
            var warmSw = Stopwatch.StartNew();
            var warmRes = await client.PostJsonAsync(
                $"/api/boards/{board.Id}/tasks/triage",
                new { title = sampleTasks[w % sampleTasks.Length].Item1, description = sampleTasks[w % sampleTasks.Length].Item2 });
            warmSw.Stop();
            var warmBody = await warmRes.Content.ReadFromJsonAsync<LiveTriageDto>();
            Console.WriteLine($"Warm-up #{w + 1}: status={warmRes.StatusCode}, roundTrip={warmSw.ElapsedMilliseconds}ms, serverLatency={warmBody?.LatencyMs}ms, degraded={warmBody?.Degraded}");
        }

        Console.WriteLine("\n=== BẮT ĐẦU ĐO CHÍNH THỨC (20 lượt) ===");
        var serverLatencies = new List<long>();
        var clientLatencies = new List<long>();
        var iterationLogs = new List<string>();
        var models = new HashSet<string>();
        var degradedCount = 0;

        for (var i = 1; i <= 20; i++)
        {
            var task = sampleTasks[(i - 1) % sampleTasks.Length];
            var sw = Stopwatch.StartNew();
            var res = await client.PostJsonAsync(
                $"/api/boards/{board.Id}/tasks/triage",
                new { title = task.Item1, description = task.Item2 });
            sw.Stop();

            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            var body = await res.Content.ReadFromJsonAsync<LiveTriageDto>();
            Assert.NotNull(body);

            var roundTrip = sw.ElapsedMilliseconds;
            var serverLatency = body!.LatencyMs;

            serverLatencies.Add(serverLatency);
            clientLatencies.Add(roundTrip);
            if (!string.IsNullOrEmpty(body.Model)) models.Add(body.Model);
            if (body.Degraded) degradedCount++;

            var line = $"Lượt {i,2:D2}: Server={serverLatency,4} ms | RoundTrip={roundTrip,4} ms | Advice={body.RoutingAdvice,-14} | Conf={body.ConfidenceScore:F2} | Degraded={body.Degraded,-5} | Domain={body.SuggestedDomain ?? "null"}";
            iterationLogs.Add(line);
            Console.WriteLine(line);
        }

        serverLatencies.Sort();
        clientLatencies.Sort();

        var p50Server = Percentile(serverLatencies, 0.50);
        var p90Server = Percentile(serverLatencies, 0.90);
        var p95Server = Percentile(serverLatencies, 0.95);
        var minServer = serverLatencies[0];
        var maxServer = serverLatencies[^1];
        var avgServer = serverLatencies.Average();

        var p50Client = Percentile(clientLatencies, 0.50);
        var p90Client = Percentile(clientLatencies, 0.90);
        var p95Client = Percentile(clientLatencies, 0.95);
        var minClient = clientLatencies[0];
        var maxClient = clientLatencies[^1];
        var avgClient = clientLatencies.Average();

        var report = new System.Text.StringBuilder();
        report.AppendLine("=== CHI TIẾT 20 LƯỢT ĐO THỰC TẾ ===");
        foreach (var l in iterationLogs) report.AppendLine(l);
        report.AppendLine("\n================= KẾT QUẢ ĐO THỰC TẾ =================");
        report.AppendLine($"Model thật: {string.Join(", ", models)}");
        report.AppendLine($"Số lượt gọi: 20 | Degraded: {degradedCount}/20");
        report.AppendLine("------------------------------------------------------");
        report.AppendLine($"SERVER LATENCY (Stopwatch đo tại JevDecisionService):");
        report.AppendLine($"  Min: {minServer} ms | P50 (Median): {p50Server} ms | Avg: {avgServer:F1} ms | P90: {p90Server} ms | P95: {p95Server} ms | Max: {maxServer} ms");
        report.AppendLine($"CLIENT ROUND-TRIP (bao gồm HTTP Pipeline + TestServer):");
        report.AppendLine($"  Min: {minClient} ms | P50 (Median): {p50Client} ms | Avg: {avgClient:F1} ms | P90: {p90Client} ms | P95: {p95Client} ms | Max: {maxClient} ms");
        report.AppendLine("======================================================");

        var logPath = Path.Combine(AppContext.BaseDirectory, "jev_results.txt");
        File.WriteAllText(logPath, report.ToString());
        Console.WriteLine(report.ToString());

        Assert.Equal(0, degradedCount);
    }

    private static double Percentile(List<long> sortedList, double percentile)
    {
        if (sortedList.Count == 0) return 0;
        var index = (int)Math.Ceiling(percentile * sortedList.Count) - 1;
        return sortedList[Math.Clamp(index, 0, sortedList.Count - 1)];
    }

    private sealed record LiveTriageOption(string Field, string Value, double Probability);
    private sealed record LiveTriageDto(
        string? SuggestedDomain,
        string? SuggestedWorkType,
        string? SuggestedPriority,
        bool NeedsSecurityReview,
        double ConfidenceScore,
        string RoutingAdvice,
        List<LiveTriageOption> Options,
        bool Degraded,
        string Model,
        long LatencyMs);
}
