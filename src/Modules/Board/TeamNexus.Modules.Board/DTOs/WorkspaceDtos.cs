using System.Text.Json;

namespace TeamNexus.Modules.Board.DTOs;

/// <summary>
/// One workspace the caller belongs to (Phase 10 §2.1).
/// <para>
/// <b>Append-only contract:</b> the first four fields (<see cref="Id"/>, <see cref="Name"/>,
/// <see cref="Description"/>, <see cref="Role"/>) are the exact payload the Giai đoạn 1 endpoint
/// served inline from <c>Program.cs</c>, and three frontend call sites parse it by name
/// (<c>DashboardPage</c>, <c>BoardView</c>, <c>ReportsPage</c>). <see cref="OwnerId"/> and
/// <see cref="IsOwner"/> were appended in Phase 10 — never reorder or rename the first four.
/// </para>
/// </summary>
public sealed record WorkspaceSummaryResponse(
    Guid Id,
    string Name,
    string? Description,
    string Role,
    Guid OwnerId,
    bool IsOwner);

/// <summary>
/// Workspace detail for the settings page (Phase 10 §2.2). <see cref="CurrentUserRole"/> lets the UI
/// decide which sections to render without a second request.
/// </summary>
public sealed record WorkspaceDetailResponse(
    Guid Id,
    string Name,
    string? Description,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    Guid OwnerId,
    string OwnerDisplayName,
    int MemberCount,
    int BoardCount,
    string CurrentUserRole);

/// <summary>Full metadata update (PUT). Same 1–120 character rule as board names.</summary>
public sealed record UpdateWorkspaceRequest(string Name, string? Description);

/// <summary>Transfer ownership to another human member of the workspace (Phase 10 §2.2).</summary>
public sealed record TransferOwnershipRequest(Guid NewOwnerId);

/// <summary>
/// One <c>activity_logs</c> row as the Activity Log UI needs it (Phase 10 §2.4).
/// <para>
/// <see cref="UserDisplayName"/> comes from a left join on <c>users</c> — <c>ActivityLog</c>
/// deliberately has no navigation property, because its FKs are declared without one so EF cannot
/// inherit the soft-delete filter of the principal (see <c>ActivityLogConfiguration</c>).
/// A null <see cref="UserId"/> means the system produced the event (the UI shows "Hệ thống").
/// </para>
/// </summary>
/// <param name="Payload">
/// Deserialized <c>jsonb</c> short diff. <c>null</c> when the row has no payload. Kept as
/// <see cref="JsonElement"/> (not <c>object</c>) so the exact JSON is forwarded verbatim.
/// </param>
public sealed record WorkspaceActivityItemResponse(
    Guid Id,
    Guid? BoardId,
    Guid? UserId,
    string? UserDisplayName,
    string EntityType,
    Guid? EntityId,
    string Action,
    JsonElement? Payload,
    DateTimeOffset CreatedAt);

/// <summary>
/// One page of activity (Phase 10 §2.4). <see cref="NextCursor"/> is opaque to the client: pass it
/// back as <c>?before=</c> to fetch the next page. <c>null</c> when there is nothing more.
/// </summary>
public sealed record WorkspaceActivityPageResponse(
    IReadOnlyList<WorkspaceActivityItemResponse> Items,
    string? NextCursor,
    bool HasMore);
