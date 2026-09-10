namespace TeamNexus.Persistence.Data.Entities;

/// <summary>
/// Junction between a task and a label. Composite PK (TaskId, LabelId).
/// Table: task_labels (Phase 2 §1 / DB design §3.4).
/// </summary>
public class TaskLabel
{
    public Guid TaskId { get; set; }

    public Guid LabelId { get; set; }

    public BoardTask? Task { get; set; }

    public Label? Label { get; set; }
}
