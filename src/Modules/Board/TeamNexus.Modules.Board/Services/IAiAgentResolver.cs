namespace TeamNexus.Modules.Board.Services;

/// <summary>
/// The workspace's AI Agent identity + its "Chờ làm rõ" column (Phase 7 §3.4, decision D7).
/// <para>
/// Declared in the <b>Board</b> module because Board must not reference the Ai module (Ai already
/// references Board) while <c>TaskService</c>/<c>WorkspaceMemberService</c> need to know who the
/// agent is. Module Ai supplies the real implementation (<c>WorkspaceAiAgentResolver</c>), which
/// overrides the no-op registered by <c>AddBoardModule</c> — exactly the
/// <see cref="IActivityLogWriter"/> trick from Phase 5 §2.1 (Program.cs calls AddBoardModule
/// before AddAiModule).
/// </para>
/// </summary>
public interface IAiAgentResolver
{
    /// <summary>
    /// The workspace's AI Agent user row, created lazily on first need (D1: exactly one per
    /// workspace, enforced by the partial unique index). Throws when the workspace does not exist.
    /// </summary>
    Task<Guid> EnsureAgentAsync(Guid workspaceId, CancellationToken ct = default);

    /// <summary>True when <paramref name="userId"/> is the <c>member_type = 'ai_agent'</c> member of this workspace.</summary>
    Task<bool> IsAiAgentAsync(Guid workspaceId, Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Ensures the board has a "Chờ làm rõ" column, creating it lazily at <c>max(position) + 1</c>
    /// (D3), and returns its id. The partial unique index <c>uq_board_columns_clarification</c> is
    /// the race guard: the loser of a concurrent create re-reads the winning column.
    /// </summary>
    Task<Guid> EnsureClarificationColumnAsync(Guid boardId, CancellationToken ct = default);
}

/// <summary>
/// No-op resolver used when the Ai module is not registered (module-scoped harnesses/tests) and as
/// the Board module's own default registration (Phase 7 §3.4).
/// <para>
/// <see cref="IsAiAgentAsync"/> answers <c>false</c> so Board read paths keep working with the
/// Board module alone; the two write-ish members throw, because silently "succeeding" without
/// creating the agent/column would hide a wiring bug.
/// </para>
/// </summary>
public sealed class NullAiAgentResolver : IAiAgentResolver
{
    public Task<Guid> EnsureAgentAsync(Guid workspaceId, CancellationToken ct = default)
        => throw new NotSupportedException("The Ai module is not registered.");

    public Task<bool> IsAiAgentAsync(Guid workspaceId, Guid userId, CancellationToken ct = default)
        => Task.FromResult(false);

    public Task<Guid> EnsureClarificationColumnAsync(Guid boardId, CancellationToken ct = default)
        => throw new NotSupportedException("The Ai module is not registered.");
}
