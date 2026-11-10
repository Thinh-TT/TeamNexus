namespace TeamNexus.Modules.Board.DTOs;

/// <summary>
/// Request payload to attach or update GitHub Pull Request and/or Issue links on a board task.
/// </summary>
public sealed record LinkTaskGitHubRequest(
    string? PrUrl,
    string? IssueUrl);
