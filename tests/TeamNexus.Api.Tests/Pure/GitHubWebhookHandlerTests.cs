namespace TeamNexus.Api.Tests.Pure;

using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using TeamNexus.Modules.Board.DTOs;
using TeamNexus.Modules.Board.Services;
using TeamNexus.Modules.Board.Services.Integrations;
using TeamNexus.Persistence.Data.Entities;
using Xunit;

/// <summary>
/// Pure unit tests for <see cref="GitHubWebhookHandler"/>.
/// Verifies payload parsing, HMAC signature validation, delivery idempotency,
/// and automated task transitions to Done upon PR merge.
/// </summary>
public sealed class GitHubWebhookHandlerTests
{
    private const string WebhookSecret = "super_secret_webhook_key_42";
    private readonly FakeSecretProtector _protector = new();
    private readonly FakeTaskService _taskService = new();

    [Fact]
    public async Task HandleWebhookAsync_EmptyBody_ReturnsBadRequest()
    {
        // Arrange
        var handler = CreateTestHandler();

        // Act
        var result = await handler.HandleWebhookAsync("ping", null, null, Array.Empty<byte>());

        // Assert
        AssertStatusCode(result, StatusCodes.Status400BadRequest);
        var doc = GetJsonPayload(result);
        Assert.Equal("Empty body", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task HandleWebhookAsync_InvalidJson_ReturnsBadRequest()
    {
        // Arrange
        var handler = CreateTestHandler();
        byte[] malformedBody = Encoding.UTF8.GetBytes("not-a-valid-json");

        // Act
        var result = await handler.HandleWebhookAsync("ping", null, null, malformedBody);

        // Assert
        AssertStatusCode(result, StatusCodes.Status400BadRequest);
        var doc = GetJsonPayload(result);
        Assert.Equal("Invalid repository payload", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task HandleWebhookAsync_MissingRepositoryFullName_ReturnsBadRequest()
    {
        // Arrange
        var handler = CreateTestHandler();
        byte[] payloadWithoutRepo = Encoding.UTF8.GetBytes("""{"action":"opened"}""");

        // Act
        var result = await handler.HandleWebhookAsync("pull_request", null, null, payloadWithoutRepo);

        // Assert
        AssertStatusCode(result, StatusCodes.Status400BadRequest);
        var doc = GetJsonPayload(result);
        Assert.Equal("Invalid repository payload", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task HandleWebhookAsync_UnknownRepository_ReturnsOkGracefulDegradation()
    {
        // Arrange
        var handler = CreateTestHandler();
        byte[] body = CreatePayload("unknown/repo");

        // Act
        var result = await handler.HandleWebhookAsync("ping", null, null, body);

        // Assert
        AssertStatusCode(result, StatusCodes.Status200OK);
        var doc = GetJsonPayload(result);
        Assert.Contains("Repository not registered or integration disabled", doc.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task HandleWebhookAsync_DisabledIntegration_ReturnsOkGracefulDegradation()
    {
        // Arrange
        var handler = CreateTestHandler();
        handler.Integrations.Add(new WorkspaceIntegration
        {
            Id = Guid.NewGuid(),
            WorkspaceId = Guid.NewGuid(),
            Provider = "github",
            RepoName = "octocat/hello-world",
            IsEnabled = false,
            EncryptedWebhookSecret = _protector.Protect(WebhookSecret)
        });

        byte[] body = CreatePayload("octocat/hello-world");

        // Act
        var result = await handler.HandleWebhookAsync("ping", null, null, body);

        // Assert
        AssertStatusCode(result, StatusCodes.Status200OK);
        var doc = GetJsonPayload(result);
        Assert.Contains("Repository not registered or integration disabled", doc.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task HandleWebhookAsync_SecretConfigured_MissingSignature_ReturnsUnauthorized()
    {
        // Arrange
        var handler = CreateTestHandler();
        handler.Integrations.Add(new WorkspaceIntegration
        {
            Id = Guid.NewGuid(),
            WorkspaceId = Guid.NewGuid(),
            Provider = "github",
            RepoName = "octocat/hello-world",
            IsEnabled = true,
            EncryptedWebhookSecret = _protector.Protect(WebhookSecret)
        });

        byte[] body = CreatePayload("octocat/hello-world");

        // Act: missing signature
        var result = await handler.HandleWebhookAsync("ping", null, null, body);

        // Assert
        AssertStatusCode(result, StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public async Task HandleWebhookAsync_SecretConfigured_InvalidSignature_ReturnsUnauthorized()
    {
        // Arrange
        var handler = CreateTestHandler();
        handler.Integrations.Add(new WorkspaceIntegration
        {
            Id = Guid.NewGuid(),
            WorkspaceId = Guid.NewGuid(),
            Provider = "github",
            RepoName = "octocat/hello-world",
            IsEnabled = true,
            EncryptedWebhookSecret = _protector.Protect(WebhookSecret)
        });

        byte[] body = CreatePayload("octocat/hello-world");
        string invalidSignature = "sha256=" + new string('0', 64);

        // Act
        var result = await handler.HandleWebhookAsync("ping", invalidSignature, null, body);

        // Assert
        AssertStatusCode(result, StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public async Task HandleWebhookAsync_PingEvent_ValidSignature_ReturnsPong()
    {
        // Arrange
        var handler = CreateTestHandler();
        handler.Integrations.Add(new WorkspaceIntegration
        {
            Id = Guid.NewGuid(),
            WorkspaceId = Guid.NewGuid(),
            Provider = "github",
            RepoName = "octocat/hello-world",
            IsEnabled = true,
            EncryptedWebhookSecret = _protector.Protect(WebhookSecret)
        });

        byte[] body = Encoding.UTF8.GetBytes("""
        {
            "zen": "Collaborate with kindness.",
            "repository": { "full_name": "octocat/hello-world" }
        }
        """);
        string signature = GitHubSignatureValidator.ComputeSignatureHeader(body, WebhookSecret);

        // Act
        var result = await handler.HandleWebhookAsync("ping", signature, "del-001", body);

        // Assert
        AssertStatusCode(result, StatusCodes.Status200OK);
        var doc = GetJsonPayload(result);
        Assert.Equal("pong", doc.RootElement.GetProperty("message").GetString());
        Assert.Equal("Collaborate with kindness.", doc.RootElement.GetProperty("zen").GetString());

        // Verify delivery was recorded
        Assert.Single(handler.DeliveryLogs);
        Assert.Equal("del-001", handler.DeliveryLogs[0].DeliveryId);
        Assert.Equal("ping", handler.DeliveryLogs[0].EventType);
    }

    [Fact]
    public async Task HandleWebhookAsync_Idempotency_DuplicateDeliveryId_ReturnsAlreadyProcessed()
    {
        // Arrange
        var handler = CreateTestHandler();
        handler.Integrations.Add(new WorkspaceIntegration
        {
            Id = Guid.NewGuid(),
            WorkspaceId = Guid.NewGuid(),
            Provider = "github",
            RepoName = "octocat/hello-world",
            IsEnabled = true,
            EncryptedWebhookSecret = _protector.Protect(WebhookSecret)
        });

        byte[] body = CreatePayload("octocat/hello-world");
        string signature = GitHubSignatureValidator.ComputeSignatureHeader(body, WebhookSecret);
        const string deliveryId = "unique-delivery-id-777";

        // First call
        var firstResult = await handler.HandleWebhookAsync("ping", signature, deliveryId, body);
        AssertStatusCode(firstResult, StatusCodes.Status200OK);
        Assert.Single(handler.DeliveryLogs);

        // Act: Second call with exact same deliveryId
        var secondResult = await handler.HandleWebhookAsync("ping", signature, deliveryId, body);

        // Assert
        AssertStatusCode(secondResult, StatusCodes.Status200OK);
        var doc = GetJsonPayload(secondResult);
        Assert.Equal("Webhook delivery already processed.", doc.RootElement.GetProperty("message").GetString());
        // Delivery log was not duplicated
        Assert.Single(handler.DeliveryLogs);
    }

    [Fact]
    public async Task HandleWebhookAsync_PullRequestNotMerged_ReturnsOkNoTransition()
    {
        // Arrange
        var handler = CreateTestHandler();
        handler.Integrations.Add(new WorkspaceIntegration
        {
            Id = Guid.NewGuid(),
            WorkspaceId = Guid.NewGuid(),
            Provider = "github",
            RepoName = "octocat/hello-world",
            IsEnabled = true,
            EncryptedWebhookSecret = _protector.Protect(WebhookSecret)
        });

        byte[] body = Encoding.UTF8.GetBytes("""
        {
            "action": "closed",
            "pull_request": {
                "number": 15,
                "merged": false,
                "html_url": "https://github.com/octocat/hello-world/pull/15"
            },
            "repository": { "full_name": "octocat/hello-world" }
        }
        """);
        string signature = GitHubSignatureValidator.ComputeSignatureHeader(body, WebhookSecret);

        // Act
        var result = await handler.HandleWebhookAsync("pull_request", signature, "del-pr-unmerged", body);

        // Assert
        AssertStatusCode(result, StatusCodes.Status200OK);
        var doc = GetJsonPayload(result);
        Assert.Contains("no task transition required", doc.RootElement.GetProperty("message").GetString());
        Assert.Empty(_taskService.CompletedCalls);
    }

    [Fact]
    public async Task HandleWebhookAsync_PullRequestMerged_MatchesTaskByPrNumber_TransitionsToDone()
    {
        // Arrange
        var workspaceId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var taskId = Guid.NewGuid();

        var handler = CreateTestHandler();
        handler.Integrations.Add(new WorkspaceIntegration
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            Provider = "github",
            RepoName = "octocat/hello-world",
            IsEnabled = true,
            EncryptedWebhookSecret = _protector.Protect(WebhookSecret)
        });

        handler.Tasks.Add(new BoardTask
        {
            Id = taskId,
            BoardId = boardId,
            Board = new TeamNexus.Persistence.Data.Entities.Board { Id = boardId, WorkspaceId = workspaceId },
            Title = "Task with PR linked",
            GithubPrNumber = 42,
            DeletedAt = null
        });

        byte[] body = Encoding.UTF8.GetBytes("""
        {
            "action": "closed",
            "pull_request": {
                "number": 42,
                "merged": true,
                "html_url": "https://github.com/octocat/hello-world/pull/42",
                "title": "Fix memory leak in parser",
                "body": "Resolves issue completely",
                "head": { "ref": "fix-leak" }
            },
            "repository": { "full_name": "octocat/hello-world" }
        }
        """);
        string signature = GitHubSignatureValidator.ComputeSignatureHeader(body, WebhookSecret);

        // Act
        var result = await handler.HandleWebhookAsync("pull_request", signature, "del-pr-merged-1", body);

        // Assert
        AssertStatusCode(result, StatusCodes.Status200OK);
        var doc = GetJsonPayload(result);
        Assert.Equal("Tasks transitioned to Done: 1", doc.RootElement.GetProperty("message").GetString());

        var taskIds = doc.RootElement.GetProperty("taskIds").EnumerateArray().Select(e => e.GetGuid()).ToList();
        Assert.Contains(taskId, taskIds);

        // Verify taskService CompleteTaskViaGitHubAsync was called
        Assert.Single(_taskService.CompletedCalls);
        var call = _taskService.CompletedCalls[0];
        Assert.Equal(taskId, call.TaskId);
        Assert.Equal("https://github.com/octocat/hello-world/pull/42", call.PrUrl);
        Assert.Equal(42, call.PrNumber);
    }

    [Fact]
    public async Task HandleWebhookAsync_PullRequestMerged_MatchesTaskByUuidInBranchOrTitle_TransitionsToDone()
    {
        // Arrange
        var workspaceId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var taskId = Guid.NewGuid();

        var handler = CreateTestHandler();
        handler.Integrations.Add(new WorkspaceIntegration
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            Provider = "github",
            RepoName = "octocat/hello-world",
            IsEnabled = true,
            EncryptedWebhookSecret = _protector.Protect(WebhookSecret)
        });

        handler.Tasks.Add(new BoardTask
        {
            Id = taskId,
            BoardId = boardId,
            Board = new TeamNexus.Persistence.Data.Entities.Board { Id = boardId, WorkspaceId = workspaceId },
            Title = "Task with UUID reference in PR",
            GithubPrNumber = null, // not linked yet
            DeletedAt = null
        });

        // PR title contains UUID of the task
        byte[] body = Encoding.UTF8.GetBytes($$"""
        {
            "action": "closed",
            "pull_request": {
                "number": 88,
                "merged": true,
                "html_url": "https://github.com/octocat/hello-world/pull/88",
                "title": "Feature implementation for {{taskId}}",
                "body": "PR closes the issue",
                "head": { "ref": "feature/task-{{taskId}}" }
            },
            "repository": { "full_name": "octocat/hello-world" }
        }
        """);
        string signature = GitHubSignatureValidator.ComputeSignatureHeader(body, WebhookSecret);

        // Act
        var result = await handler.HandleWebhookAsync("pull_request", signature, "del-pr-merged-uuid", body);

        // Assert
        AssertStatusCode(result, StatusCodes.Status200OK);
        var doc = GetJsonPayload(result);
        Assert.Equal("Tasks transitioned to Done: 1", doc.RootElement.GetProperty("message").GetString());

        var taskIds = doc.RootElement.GetProperty("taskIds").EnumerateArray().Select(e => e.GetGuid()).ToList();
        Assert.Contains(taskId, taskIds);

        Assert.Single(_taskService.CompletedCalls);
        Assert.Equal(taskId, _taskService.CompletedCalls[0].TaskId);
    }

    [Fact]
    public async Task HandleWebhookAsync_PullRequestMerged_MatchesTaskByShortHexCode_TransitionsToDone()
    {
        // Arrange
        var workspaceId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var taskId = Guid.Parse("f3ffe732-6e99-4f90-a718-fb47d1fd5846");
        var shortCode = "#F3FFE732";

        var handler = CreateTestHandler();
        handler.Integrations.Add(new WorkspaceIntegration
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            Provider = "github",
            RepoName = "octocat/hello-world",
            IsEnabled = true,
            EncryptedWebhookSecret = _protector.Protect(WebhookSecret)
        });

        handler.Tasks.Add(new BoardTask
        {
            Id = taskId,
            BoardId = boardId,
            Board = new TeamNexus.Persistence.Data.Entities.Board { Id = boardId, WorkspaceId = workspaceId },
            Title = "Task with short code #F3FFE732 reference in PR",
            GithubPrNumber = null,
            DeletedAt = null
        });

        // PR title contains short code #F3FFE732
        byte[] body = Encoding.UTF8.GetBytes($$"""
        {
            "action": "closed",
            "pull_request": {
                "number": 99,
                "merged": true,
                "html_url": "https://github.com/octocat/hello-world/pull/99",
                "title": "feat: resolve task {{shortCode}} in sprint",
                "body": "This PR addresses {{shortCode}} automatically",
                "head": { "ref": "feature/F3FFE732-ui-chip" }
            },
            "repository": { "full_name": "octocat/hello-world" }
        }
        """);
        string signature = GitHubSignatureValidator.ComputeSignatureHeader(body, WebhookSecret);

        // Act
        var result = await handler.HandleWebhookAsync("pull_request", signature, "del-pr-merged-shortcode", body);

        // Assert
        AssertStatusCode(result, StatusCodes.Status200OK);
        var doc = GetJsonPayload(result);
        Assert.Equal("Tasks transitioned to Done: 1", doc.RootElement.GetProperty("message").GetString());

        var taskIds = doc.RootElement.GetProperty("taskIds").EnumerateArray().Select(e => e.GetGuid()).ToList();
        Assert.Contains(taskId, taskIds);

        Assert.Single(_taskService.CompletedCalls);
        Assert.Equal(taskId, _taskService.CompletedCalls[0].TaskId);
    }

    [Fact]
    public async Task HandleWebhookAsync_PullRequestMerged_NoMatchingTask_ReturnsOkNoTaskFound()
    {
        // Arrange
        var workspaceId = Guid.NewGuid();
        var handler = CreateTestHandler();
        handler.Integrations.Add(new WorkspaceIntegration
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            Provider = "github",
            RepoName = "octocat/hello-world",
            IsEnabled = true,
            EncryptedWebhookSecret = _protector.Protect(WebhookSecret)
        });

        byte[] body = Encoding.UTF8.GetBytes("""
        {
            "action": "closed",
            "pull_request": {
                "number": 555,
                "merged": true,
                "html_url": "https://github.com/octocat/hello-world/pull/555",
                "title": "Unrelated chore",
                "body": "No task references here",
                "head": { "ref": "chore-docs" }
            },
            "repository": { "full_name": "octocat/hello-world" }
        }
        """);
        string signature = GitHubSignatureValidator.ComputeSignatureHeader(body, WebhookSecret);

        // Act
        var result = await handler.HandleWebhookAsync("pull_request", signature, "del-pr-notask", body);

        // Assert
        AssertStatusCode(result, StatusCodes.Status200OK);
        var doc = GetJsonPayload(result);
        Assert.Equal("No associated board task found for PR #555 in workspace.", doc.RootElement.GetProperty("message").GetString());
        Assert.Empty(_taskService.CompletedCalls);
    }

    [Fact]
    public async Task HandleWebhookAsync_OtherEventType_ReturnsOkLogged()
    {
        // Arrange
        var handler = CreateTestHandler();
        handler.Integrations.Add(new WorkspaceIntegration
        {
            Id = Guid.NewGuid(),
            WorkspaceId = Guid.NewGuid(),
            Provider = "github",
            RepoName = "octocat/hello-world",
            IsEnabled = true,
            EncryptedWebhookSecret = _protector.Protect(WebhookSecret)
        });

        byte[] body = CreatePayload("octocat/hello-world");
        string signature = GitHubSignatureValidator.ComputeSignatureHeader(body, WebhookSecret);

        // Act
        var result = await handler.HandleWebhookAsync("star", signature, "del-star", body);

        // Assert
        AssertStatusCode(result, StatusCodes.Status200OK);
        var doc = GetJsonPayload(result);
        Assert.Equal("Event 'star' received and logged.", doc.RootElement.GetProperty("message").GetString());
    }

    // Helper Factory
    private TestableGitHubWebhookHandler CreateTestHandler()
    {
        return new TestableGitHubWebhookHandler(_protector, _taskService);
    }

    private static byte[] CreatePayload(string repoFullName)
    {
        return Encoding.UTF8.GetBytes($$"""
        {
            "repository": { "full_name": "{{repoFullName}}" }
        }
        """);
    }

    private static void AssertStatusCode(IResult result, int expectedStatusCode)
    {
        var statusResult = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(expectedStatusCode, statusResult.StatusCode);
    }

    private static JsonDocument GetJsonPayload(IResult result)
    {
        var valueResult = Assert.IsAssignableFrom<IValueHttpResult>(result);
        Assert.NotNull(valueResult.Value);
        var json = JsonSerializer.Serialize(valueResult.Value);
        return JsonDocument.Parse(json);
    }

    // In-memory test double
    private sealed class TestableGitHubWebhookHandler : GitHubWebhookHandler
    {
        public List<WorkspaceIntegration> Integrations { get; } = new();
        public List<WebhookDeliveryLog> DeliveryLogs { get; } = new();
        public List<BoardTask> Tasks { get; } = new();

        public TestableGitHubWebhookHandler(
            IIntegrationSecretProtector protector,
            ITaskService taskService)
            : base(null!, protector, taskService, NullLogger<GitHubWebhookHandler>.Instance)
        {
        }

        protected override Task<WorkspaceIntegration?> FindIntegrationAsync(string repoFullName, CancellationToken ct)
        {
            var normalized = repoFullName.ToLowerInvariant();
            var integration = Integrations.FirstOrDefault(i =>
                i.IsEnabled &&
                string.Equals(i.Provider, "github", StringComparison.OrdinalIgnoreCase) &&
                i.RepoName != null &&
                string.Equals(i.RepoName, normalized, StringComparison.OrdinalIgnoreCase));

            return Task.FromResult(integration);
        }

        protected override Task<bool> IsDeliveryProcessedAsync(string deliveryId, CancellationToken ct)
        {
            var exists = DeliveryLogs.Any(l => string.Equals(l.DeliveryId, deliveryId, StringComparison.Ordinal));
            return Task.FromResult(exists);
        }

        protected override Task RecordDeliveryLogAsync(WebhookDeliveryLog deliveryLog, CancellationToken ct)
        {
            DeliveryLogs.Add(deliveryLog);
            return Task.CompletedTask;
        }

        protected override Task<List<BoardTask>> FindTasksByPrNumberAsync(Guid workspaceId, int prNumber, CancellationToken ct)
        {
            var matched = Tasks.Where(t =>
                t.DeletedAt == null &&
                t.Board != null &&
                t.Board.WorkspaceId == workspaceId &&
                t.GithubPrNumber == prNumber).ToList();

            return Task.FromResult(matched);
        }

        protected override Task<List<BoardTask>> FindTasksByIdsAsync(Guid workspaceId, IReadOnlyCollection<Guid> taskIds, CancellationToken ct)
        {
            var idSet = taskIds.ToHashSet();
            var matched = Tasks.Where(t =>
                t.DeletedAt == null &&
                t.Board != null &&
                t.Board.WorkspaceId == workspaceId &&
                idSet.Contains(t.Id)).ToList();

            return Task.FromResult(matched);
        }

        protected override Task<List<BoardTask>> FindTasksByShortCodesAsync(Guid workspaceId, IReadOnlyCollection<string> shortCodes, CancellationToken ct)
        {
            var normalized = shortCodes.Select(c => c.ToLowerInvariant()).ToHashSet();
            var matched = Tasks.Where(t =>
                t.DeletedAt == null &&
                t.Board != null &&
                t.Board.WorkspaceId == workspaceId &&
                normalized.Contains(t.Id.ToString("N")[..8].ToLowerInvariant())).ToList();

            return Task.FromResult(matched);
        }
    }

    private sealed class FakeSecretProtector : IIntegrationSecretProtector
    {
        public string Protect(string? plaintext) => $"enc_{plaintext}";

        public string Unprotect(string? ciphertext)
        {
            if (string.IsNullOrEmpty(ciphertext)) return string.Empty;
            if (ciphertext.StartsWith("enc_", StringComparison.Ordinal))
            {
                return ciphertext["enc_".Length..];
            }
            return ciphertext;
        }
    }

    private sealed class FakeTaskService : ITaskService
    {
        public List<(Guid TaskId, string PrUrl, int PrNumber)> CompletedCalls { get; } = new();

        public Task<TaskResponse> CompleteTaskViaGitHubAsync(Guid taskId, string prUrl, int prNumber, CancellationToken ct = default)
        {
            CompletedCalls.Add((taskId, prUrl, prNumber));
            return Task.FromResult<TaskResponse>(null!);
        }

        public Task<IReadOnlyList<TaskResponse>> GetTasksAsync(Guid boardId, Guid? columnId, Guid userId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<TaskResponse> GetTaskByIdAsync(Guid taskId, Guid userId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<TaskResponse> CreateTaskAsync(Guid boardId, CreateTaskRequest request, Guid userId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<TaskResponse> UpdateTaskAsync(Guid taskId, UpdateTaskRequest request, Guid userId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<TaskResponse> MoveTaskAsync(Guid taskId, MoveTaskRequest request, Guid userId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task DeleteTaskAsync(Guid taskId, Guid userId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<TaskResponse> UpdateGitHubLinkAsync(Guid taskId, LinkTaskGitHubRequest request, Guid userId, CancellationToken ct = default) => throw new NotImplementedException();
    }
}
