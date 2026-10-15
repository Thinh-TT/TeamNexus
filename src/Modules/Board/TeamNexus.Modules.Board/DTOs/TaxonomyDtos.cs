namespace TeamNexus.Modules.Board.DTOs;

/// <summary>
/// One taxonomy entry as returned to a client (Giai đoạn 14.7 §A2).
/// <para>
/// <paramref name="Key"/> is the stable machine value a task/board stores and the value Jev receives as a
/// <c>Choice</c> option; <paramref name="Label"/> is what a human reads. Keeping them separate is what
/// makes renaming a label a display change rather than a data migration.
/// </para>
/// </summary>
public sealed record TaxonomyEntryResponse(
    Guid Id,
    string Kind,
    string Key,
    string Label,
    string? ColorHex,
    int Position);

/// <summary>
/// The whole workspace vocabulary, split by kind so a client never has to filter
/// (Giai đoạn 14.7 §A2).
/// </summary>
/// <param name="Seeded">
/// True when this read is what created the defaults. Surfaced so the UI can tell "you have the standard
/// vocabulary" apart from "someone deliberately curated this".
/// </param>
public sealed record WorkspaceTaxonomyResponse(
    Guid WorkspaceId,
    IReadOnlyList<TaxonomyEntryResponse> Domains,
    IReadOnlyList<TaxonomyEntryResponse> WorkTypes,
    bool Seeded);

/// <summary>One entry the client wants to exist after the call (Giai đoạn 14.7 §A2).</summary>
/// <param name="ColorHex">
/// Optional <c>#RRGGBB</c> badge colour. Defaulted so a colourless entry is just two fields — and so an
/// omitted field binds as "no colour" rather than as an unbound required parameter.
/// </param>
public sealed record TaxonomyEntryRequest(
    string Key,
    string Label,
    string? ColorHex = null);

/// <summary>
/// Replace-the-whole-vocabulary request. A replace (not a patch) keeps the operation idempotent and avoids
/// a diff protocol: the client sends what it wants to see.
/// </summary>
public sealed record UpdateTaxonomyRequest(
    IReadOnlyList<TaxonomyEntryRequest>? Domains,
    IReadOnlyList<TaxonomyEntryRequest>? WorkTypes);

/// <summary>
/// Capacity-profile update for one member (Giai đoạn 14.7 §A2).
/// <para>
/// Every field is nullable and an <b>absent</b> field means "leave unchanged" — the Phase 13 §P4 precedent,
/// so a client that only edits skills cannot accidentally reset a colleague's WIP limit.
/// </para>
/// </summary>
public sealed record UpdateMemberProfileRequest(
    IReadOnlyList<string>? Skills,
    string? PrimaryRole,
    double? CapacityWeight,
    int? MaxWipLimit);
