namespace TeamNexus.Modules.Ai.Services.Agent;

/// <summary>
/// jsonb shape of <c>ai_action_logs.after_snapshot</c> for a <c>PostComment</c> action (Phase 7 §4.7).
/// </summary>
public sealed record AgentCommentSnapshot(Guid TaskId, string Content);

/// <summary>
/// jsonb shape of <c>ai_action_logs.after_snapshot</c> for a <c>PostAttachment</c> action
/// (Phase 7 §4.7). <c>contentBase64</c> is the file body: base64 inflates the row by ~1.37×, which is
/// exactly why <c>Agent:MaxDraftChars</c> caps the draft at 384 000 characters (≈512 KB once encoded).
/// </summary>
public sealed record AgentAttachmentSnapshot(
    Guid TaskId,
    string FileName,
    string? ContentType,
    string ContentBase64,
    Guid? SourceRunId);
