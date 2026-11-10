namespace TeamNexus.Persistence.Data.Entities;

/// <summary>
/// Links a <see cref="BoardTask"/> to an external GitHub Pull Request or Issue (Phase 15.1 §T15.1.2 / 15.3).
/// Table: <c>task_external_links</c>.
/// </summary>
public class TaskExternalLink : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid WorkspaceId { get; set; }

    public Guid TaskId { get; set; }

    public string Provider { get; set; } = "github";

    /// <summary>Artifact type: <c>"pull_request"</c> or <c>"issue"</c>.</summary>
    public string LinkType { get; set; } = string.Empty;

    /// <summary>Stable external key, e.g. <c>"owner/repo#42"</c>.</summary>
    public string ExternalId { get; set; } = string.Empty;

    public string ExternalUrl { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    /// <summary>Artifact state: <c>"draft"</c>, <c>"open"</c>, <c>"merged"</c>, or <c>"closed"</c>.</summary>
    public string State { get; set; } = "open";

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public BoardTask? Task { get; set; }
}

