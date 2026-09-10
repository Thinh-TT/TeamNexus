namespace TeamNexus.Persistence.Data.Entities;

/// <summary>
/// A Kanban column (status lane) inside a board. Order is by <see cref="Position"/>.
/// Table: board_columns (Phase 2 §1 / DB design §3.4).
/// No DeletedAt — a column is hidden when its board is soft-deleted (see config filter).
/// </summary>
public class BoardColumn : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid BoardId { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Display order within the board. Unique per board (UQ (BoardId, Position)).</summary>
    public int Position { get; set; }

    /// <summary>Marks this column as the "done" lane: tasks moved here get CompletedAt set.</summary>
    public bool IsDone { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public Board? Board { get; set; }
}
