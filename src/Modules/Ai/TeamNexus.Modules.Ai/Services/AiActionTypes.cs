namespace TeamNexus.Modules.Ai.Services;

/// <summary>
/// Action types stored in <c>ai_action_logs.action</c> (Accountability Layer, Phase 4 §2.1).
/// The column is free text (DB design §4): new AI write actions are added here — each one needs
/// its own <see cref="IAiActionApplier"/> so <see cref="AiActionService"/> stays the single gateway.
/// </summary>
public static class AiActionTypes
{
    /// <summary>Smart Setup confirmation: create a batch of sub-tasks (Phase 3 → Phase 4 handover).</summary>
    public const string CreateSubtasks = "CreateSubtasks";
}

/// <summary>Entity types stored in <c>ai_action_logs.entity_type</c>.</summary>
public static class AiEntityTypes
{
    /// <summary>Batch action scoped to a board: <c>entity_id</c> = boardId.</summary>
    public const string Board = "Board";
}
