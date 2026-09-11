using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using TeamNexus.Modules.Ai.Services.Agent;
using TeamNexus.Modules.Board.Endpoints;
using TeamNexus.Shared.Endpoints;

namespace TeamNexus.Modules.Ai.Endpoints;

/// <summary>
/// AI Agent Executor endpoints (Phase 7 §4.9), routes 1–5.
/// <para>
/// Authorization is enforced <b>inside the service</b> (<c>IWorkspaceAccess.RequireManagerAsync</c> for
/// the writes, <c>RequireMemberAsync</c> for the reads) — consistent with Phase 4/5/6, where the
/// endpoint only authenticates, binds and returns. The three write routes additionally require the
/// anti-CSRF header and are the ones gated by <c>Agent:Enabled</c> (503).
/// </para>
/// </summary>
public static class AgentRunEndpoints
{
    public static IEndpointRouteBuilder MapAgentRunEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // 1–3) Task-scoped: start a run, re-run after a clarification answer, list the history.
        var taskGroup = endpoints.MapGroup("/api/tasks/{taskId:guid}/agent-runs")
            .WithTags("AgentRuns")
            .AddEndpointFilter<DomainExceptionFilter>();

        taskGroup.MapPost("/", StartAsync)
            .RequireAuthorization()
            .AddEndpointFilter<AntiforgeryValidationEndpointFilter>();

        taskGroup.MapPost("/{runId:guid}/rerun", RerunAsync)
            .RequireAuthorization()
            .AddEndpointFilter<AntiforgeryValidationEndpointFilter>();

        taskGroup.MapGet("/", ListAsync).RequireAuthorization();

        // 4–5) Run-scoped: detail + cancel.
        var runGroup = endpoints.MapGroup("/api/agent-runs/{runId:guid}")
            .WithTags("AgentRuns")
            .AddEndpointFilter<DomainExceptionFilter>();

        runGroup.MapGet("/", GetAsync).RequireAuthorization();

        runGroup.MapPost("/cancel", CancelAsync)
            .RequireAuthorization()
            .AddEndpointFilter<AntiforgeryValidationEndpointFilter>();

        return endpoints;
    }

    // ---- handlers ----------------------------------------------------------

    /// <summary>
    /// <b>202 Accepted</b>, not 200: the loop runs in its own scope because
    /// <c>Agent:RunTimeoutSeconds</c> (default 300s) far exceeds any proxy's HTTP timeout. The response
    /// body is the freshly inserted <c>Running</c> row.
    /// </summary>
    private static async Task<IResult> StartAsync(
        Guid taskId,
        HttpContext http,
        IAgentRunService runs,
        CancellationToken ct)
    {
        var run = await runs.StartAsync(taskId, http.RequireUserId(), previousRunId: null, ct);
        return Results.Accepted($"/api/agent-runs/{run.Id}", run);
    }

    /// <summary>"Chạy lại" after the team lead answered a clarification question (D14: new row).</summary>
    private static async Task<IResult> RerunAsync(
        Guid taskId,
        Guid runId,
        HttpContext http,
        IAgentRunService runs,
        CancellationToken ct)
    {
        var run = await runs.StartAsync(taskId, http.RequireUserId(), previousRunId: runId, ct);
        return Results.Accepted($"/api/agent-runs/{run.Id}", run);
    }

    /// <summary>
    /// <c>take</c> is parsed defensively: a non-numeric value falls back to the default instead of
    /// failing binding, and the service clamps the range to 1..50.
    /// </summary>
    private static async Task<IResult> ListAsync(
        Guid taskId,
        string? take,
        HttpContext http,
        IAgentRunService runs,
        CancellationToken ct)
    {
        var parsed = int.TryParse(take, out var value) && value > 0
            ? Math.Clamp(value, 1, AgentEndpointHelpers.MaxRunsTake)
            : AgentEndpointHelpers.DefaultRunsTake;

        var history = await runs.ListAsync(taskId, parsed, http.RequireUserId(), ct);
        return Results.Ok(history);
    }

    private static async Task<IResult> GetAsync(
        Guid runId,
        HttpContext http,
        IAgentRunService runs,
        CancellationToken ct)
        => Results.Ok(await runs.GetAsync(runId, http.RequireUserId(), ct));

    private static async Task<IResult> CancelAsync(
        Guid runId,
        HttpContext http,
        IAgentRunService runs,
        CancellationToken ct)
        => Results.Ok(await runs.CancelAsync(runId, http.RequireUserId(), ct));
}
