using System.Collections;
using System.Linq.Expressions;
using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.Logging.Abstractions;
using TeamNexus.Modules.Board.DTOs;
using TeamNexus.Modules.Board.Services;
using TeamNexus.Modules.Board.Services.Integrations;
using TeamNexus.Persistence.Data;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Api.Tests.Pure;

/// <summary>
/// Pure unit tests for <see cref="GitHubIntegrationService"/>.
/// Validates PAT masking, repository name formatting, secret protection,
/// CRUD logic, and GitHub REST API connectivity testing without a real database.
/// </summary>
public sealed class GitHubIntegrationServiceTests
{
    private readonly EphemeralDataProtectionProvider _dataProtectionProvider = new();
    private readonly IntegrationSecretProtector _protector;
    private readonly FakeWorkspaceAccess _access = new();
    private readonly List<WorkspaceIntegration> _store = new();
    private readonly TestTeamNexusDbContext _db;
    private HttpStatusCode _mockHttpStatus = HttpStatusCode.OK;
    private string _mockHttpResponseBody = JsonSerializer.Serialize(new
    {
        full_name = "test-owner/test-repo",
        default_branch = "main"
    });
    private bool _mockHttpThrows;

    public GitHubIntegrationServiceTests()
    {
        _protector = new IntegrationSecretProtector(_dataProtectionProvider);
        _db = new TestTeamNexusDbContext(_store);
    }

    private GitHubIntegrationService CreateService()
    {
        var handler = new MockHttpMessageHandler((req, ct) =>
        {
            if (_mockHttpThrows)
            {
                throw new HttpRequestException("Network failure connecting to api.github.com");
            }

            var response = new HttpResponseMessage(_mockHttpStatus)
            {
                Content = new StringContent(_mockHttpResponseBody, System.Text.Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        });

        var factory = new FakeHttpClientFactory(handler);

        return new GitHubIntegrationService(
            _db,
            _access,
            _protector,
            factory,
            NullLogger<GitHubIntegrationService>.Instance);
    }

    // =========================================================================
    // 1. MaskPat Tests
    // =========================================================================

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    public void MaskPat_NullOrWhitespace_ReturnsNull(string? input, string? expected)
    {
        var result = GitHubIntegrationService.MaskPat(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("a", "•")]
    [InlineData("ab", "••")]
    [InlineData("abc", "•••")]
    [InlineData("abcd", "••••")]
    public void MaskPat_ShortToken_ReturnsAllBullets(string input, string expected)
    {
        var result = GitHubIntegrationService.MaskPat(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("ghp_1234567890abcdef", "ghp_••••••••cdef")]
    [InlineData("ghp_ABCDEFGHIJKLMN9999", "ghp_••••••••9999")]
    public void MaskPat_ClassicGitHubPat_PreservesPrefixAndLast4WithEightBullets(string input, string expected)
    {
        var result = GitHubIntegrationService.MaskPat(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("github_pat_11AAAAAAA_xxxxxxxxxxxx1234", "github_pat_••••••••1234")]
    public void MaskPat_FineGrainedGitHubPat_PreservesPrefixAndLast4WithEightBullets(string input, string expected)
    {
        var result = GitHubIntegrationService.MaskPat(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("custom_secret_key_8888", "••••8888")]
    [InlineData("12345678", "••••5678")]
    public void MaskPat_OtherToken_PreservesLast4WithFourBullets(string input, string expected)
    {
        var result = GitHubIntegrationService.MaskPat(input);
        Assert.Equal(expected, result);
    }

    // =========================================================================
    // 2. RepoName Validation Tests
    // =========================================================================

    [Theory]
    [InlineData("facebook/react")]
    [InlineData("dotnet/aspnetcore")]
    [InlineData("my-org/my-repo-123")]
    [InlineData("user.name/repo.name")]
    public void ValidateRepoName_ValidFormat_DoesNotThrow(string validRepo)
    {
        GitHubIntegrationService.ValidateRepoName(validRepo);
    }

    [Theory]
    [InlineData("invalid-repo-without-slash")]
    [InlineData("owner/repo/extra")]
    [InlineData("/repo")]
    [InlineData("owner/")]
    [InlineData("/")]
    [InlineData("owner//repo")]
    public void ValidateRepoName_InvalidFormat_ThrowsBadRequestException(string invalidRepo)
    {
        var ex = Assert.Throws<BadRequestException>(() => GitHubIntegrationService.ValidateRepoName(invalidRepo));
        Assert.Contains("owner/repo", ex.Message);
    }

    [Fact]
    public async Task UpsertAsync_InvalidRepoName_ThrowsBadRequestException()
    {
        var service = CreateService();
        var wsId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var request = new UpdateGitHubIntegrationRequest(
            RepoName: "invalid_repo_name",
            PersonalAccessToken: null,
            WebhookSecret: null,
            IsEnabled: true);

        var ex = await Assert.ThrowsAsync<BadRequestException>(() =>
            service.UpsertAsync(wsId, userId, request));

        Assert.Contains("owner/repo", ex.Message);
    }

    // =========================================================================
    // 3. Secret Protection & Plaintext Leaks Prevention Tests
    // =========================================================================

    [Fact]
    public async Task UpsertAsync_EncryptsPatAndWebhookSecret_NeverLeaksPlaintextInResponseOrStore()
    {
        var service = CreateService();
        var wsId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        const string rawPat = "ghp_super_secret_pat_value_1234";
        const string rawSecret = "whsec_super_secret_webhook_key_5678";

        var request = new UpdateGitHubIntegrationRequest(
            RepoName: "facebook/react",
            PersonalAccessToken: rawPat,
            WebhookSecret: rawSecret,
            IsEnabled: true);

        var response = await service.UpsertAsync(wsId, userId, request);

        // 1. Response must never contain plaintext secrets
        Assert.True(response.HasPat);
        Assert.True(response.HasWebhookSecret);
        Assert.Equal("ghp_••••••••1234", response.MaskedPat);
        Assert.DoesNotContain(rawPat, response.MaskedPat);

        // 2. Underlying entity in DB must be encrypted
        var stored = Assert.Single(_store);
        Assert.NotEqual(rawPat, stored.EncryptedPat);
        Assert.NotEqual(rawSecret, stored.EncryptedWebhookSecret);
        Assert.False(string.IsNullOrWhiteSpace(stored.EncryptedPat));
        Assert.False(string.IsNullOrWhiteSpace(stored.EncryptedWebhookSecret));

        // 3. Decryption via protector successfully recovers plaintext
        Assert.Equal(rawPat, _protector.Unprotect(stored.EncryptedPat));
        Assert.Equal(rawSecret, _protector.Unprotect(stored.EncryptedWebhookSecret));
    }

    // =========================================================================
    // 4. GetAsync Tests
    // =========================================================================

    [Fact]
    public async Task GetAsync_WhenIntegrationDoesNotExist_ReturnsNull()
    {
        var service = CreateService();
        var result = await service.GetAsync(Guid.NewGuid(), Guid.NewGuid());
        Assert.Null(result);
    }

    [Fact]
    public async Task GetAsync_WhenIntegrationExists_ReturnsMappedDto()
    {
        var service = CreateService();
        var wsId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        _store.Add(new WorkspaceIntegration
        {
            WorkspaceId = wsId,
            Provider = "github",
            RepoName = "owner/repo",
            EncryptedPat = _protector.Protect("ghp_sample_token_abcd"),
            EncryptedWebhookSecret = _protector.Protect("secret123"),
            IsEnabled = true,
            UpdatedAt = DateTimeOffset.UtcNow
        });

        var result = await service.GetAsync(wsId, userId);

        Assert.NotNull(result);
        Assert.Equal(wsId, result.WorkspaceId);
        Assert.Equal("github", result.Provider);
        Assert.Equal("owner/repo", result.RepoName);
        Assert.True(result.HasPat);
        Assert.Equal("ghp_••••••••abcd", result.MaskedPat);
        Assert.True(result.HasWebhookSecret);
        Assert.True(result.IsEnabled);
        Assert.Equal("/api/webhooks/github", result.WebhookUrl);
    }

    [Fact]
    public async Task GetAsync_NonManagerCaller_ThrowsForbiddenException()
    {
        var service = CreateService();
        _access.IsManager = false;

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.GetAsync(Guid.NewGuid(), Guid.NewGuid()));
    }

    // =========================================================================
    // 5. UpsertAsync Tests
    // =========================================================================

    [Fact]
    public async Task UpsertAsync_NonManagerCaller_ThrowsForbiddenException()
    {
        var service = CreateService();
        _access.IsManager = false;

        var request = new UpdateGitHubIntegrationRequest("owner/repo", null, null, null);
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.UpsertAsync(Guid.NewGuid(), Guid.NewGuid(), request));
    }

    [Fact]
    public async Task UpsertAsync_UpdateExistingIntegration_RetainsExistingSecretsWhenOmitted()
    {
        var service = CreateService();
        var wsId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var initialRequest = new UpdateGitHubIntegrationRequest(
            RepoName: "org/initial-repo",
            PersonalAccessToken: "ghp_original_token_1111",
            WebhookSecret: "original_secret",
            IsEnabled: true);

        await service.UpsertAsync(wsId, userId, initialRequest);

        // Update only repo name and IsEnabled, omit secrets
        var updateRequest = new UpdateGitHubIntegrationRequest(
            RepoName: "org/updated-repo",
            PersonalAccessToken: null,
            WebhookSecret: "",
            IsEnabled: false);

        var updatedResponse = await service.UpsertAsync(wsId, userId, updateRequest);

        Assert.Equal("org/updated-repo", updatedResponse.RepoName);
        Assert.False(updatedResponse.IsEnabled);
        Assert.True(updatedResponse.HasPat);
        Assert.Equal("ghp_••••••••1111", updatedResponse.MaskedPat);
        Assert.True(updatedResponse.HasWebhookSecret);

        var stored = Assert.Single(_store);
        Assert.Equal("ghp_original_token_1111", _protector.Unprotect(stored.EncryptedPat));
        Assert.Equal("original_secret", _protector.Unprotect(stored.EncryptedWebhookSecret));
    }

    // =========================================================================
    // 6. DeleteAsync Tests
    // =========================================================================

    [Fact]
    public async Task DeleteAsync_WhenIntegrationExists_RemovesFromDatabase()
    {
        var service = CreateService();
        var wsId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        _store.Add(new WorkspaceIntegration
        {
            WorkspaceId = wsId,
            Provider = "github",
            RepoName = "owner/repo"
        });

        Assert.Single(_store);

        await service.DeleteAsync(wsId, userId);

        Assert.Empty(_store);
    }

    [Fact]
    public async Task DeleteAsync_WhenIntegrationDoesNotExist_IsNoOp()
    {
        var service = CreateService();
        await service.DeleteAsync(Guid.NewGuid(), Guid.NewGuid());
        Assert.Empty(_store);
    }

    [Fact]
    public async Task DeleteAsync_NonManagerCaller_ThrowsForbiddenException()
    {
        var service = CreateService();
        _access.IsManager = false;

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.DeleteAsync(Guid.NewGuid(), Guid.NewGuid()));
    }

    // =========================================================================
    // 7. TestConnectionAsync Tests
    // =========================================================================

    [Fact]
    public async Task TestConnectionAsync_NonManagerCaller_ThrowsForbiddenException()
    {
        var service = CreateService();
        _access.IsManager = false;

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.TestConnectionAsync(Guid.NewGuid(), Guid.NewGuid()));
    }

    [Fact]
    public async Task TestConnectionAsync_NotConfigured_ReturnsFalseWithHelpfulMessage()
    {
        var service = CreateService();
        var wsId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var result = await service.TestConnectionAsync(wsId, userId);

        Assert.False(result.Success);
        Assert.Contains("Chưa cấu hình", result.ErrorMessage);
    }

    [Fact]
    public async Task TestConnectionAsync_SuccessfulGitHubResponse_ReturnsSuccessWithDetails()
    {
        var service = CreateService();
        var wsId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        _store.Add(new WorkspaceIntegration
        {
            WorkspaceId = wsId,
            Provider = "github",
            RepoName = "facebook/react",
            EncryptedPat = _protector.Protect("ghp_valid_token_1234")
        });

        _mockHttpStatus = HttpStatusCode.OK;
        _mockHttpResponseBody = JsonSerializer.Serialize(new
        {
            full_name = "facebook/react",
            default_branch = "main"
        });

        var result = await service.TestConnectionAsync(wsId, userId);

        Assert.True(result.Success);
        Assert.Equal("facebook/react", result.RepoFullName);
        Assert.Equal("main", result.DefaultBranch);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public async Task TestConnectionAsync_UnauthorizedGitHubResponse_ReturnsFailureWithErrorMessage()
    {
        var service = CreateService();
        var wsId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        _store.Add(new WorkspaceIntegration
        {
            WorkspaceId = wsId,
            Provider = "github",
            RepoName = "private/secret-repo",
            EncryptedPat = _protector.Protect("ghp_invalid_token")
        });

        _mockHttpStatus = HttpStatusCode.Unauthorized;
        _mockHttpResponseBody = JsonSerializer.Serialize(new
        {
            message = "Bad credentials"
        });

        var result = await service.TestConnectionAsync(wsId, userId);

        Assert.False(result.Success);
        Assert.Contains("Bad credentials", result.ErrorMessage);
    }

    [Fact]
    public async Task TestConnectionAsync_NetworkFailure_DegradesGracefullyWithoutThrowing()
    {
        var service = CreateService();
        var wsId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        _store.Add(new WorkspaceIntegration
        {
            WorkspaceId = wsId,
            Provider = "github",
            RepoName = "owner/repo",
            EncryptedPat = _protector.Protect("ghp_token")
        });

        _mockHttpThrows = true;

        var result = await service.TestConnectionAsync(wsId, userId);

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
        Assert.StartsWith("Lỗi kết nối GitHub:", result.ErrorMessage);
    }

    // =========================================================================
    // Test Doubles & Helpers
    // =========================================================================

    private sealed class FakeWorkspaceAccess : IWorkspaceAccess
    {
        public bool IsManager { get; set; } = true;

        public Task<WorkspaceMember> RequireMemberAsync(Guid workspaceId, Guid userId, CancellationToken ct = default)
        {
            if (!IsManager)
            {
                throw new ForbiddenException("Requires Member role.");
            }
            return Task.FromResult(new WorkspaceMember { WorkspaceId = workspaceId, UserId = userId, Role = WorkspaceRole.Member });
        }

        public Task<WorkspaceMember> RequireManagerAsync(Guid workspaceId, Guid userId, CancellationToken ct = default)
        {
            if (!IsManager)
            {
                throw new ForbiddenException("Requires Manager or Admin role in this workspace.");
            }
            return Task.FromResult(new WorkspaceMember { WorkspaceId = workspaceId, UserId = userId, Role = WorkspaceRole.Manager });
        }
    }

    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

        public MockHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => _handler(request, cancellationToken);
    }

    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public FakeHttpClientFactory(HttpMessageHandler handler)
        {
            _handler = handler;
        }

        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private sealed class TestTeamNexusDbContext : TeamNexusDbContext
    {
        private readonly DbSet<WorkspaceIntegration> _workspaceIntegrations;

        public TestTeamNexusDbContext(List<WorkspaceIntegration> list)
            : base(new DbContextOptionsBuilder<TeamNexusDbContext>().Options)
        {
            _workspaceIntegrations = new TestDbSet<WorkspaceIntegration>(list);
        }

        public override DbSet<TEntity> Set<TEntity>() where TEntity : class
        {
            if (typeof(TEntity) == typeof(WorkspaceIntegration))
            {
                return (_workspaceIntegrations as DbSet<TEntity>)!;
            }

            return base.Set<TEntity>();
        }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(1);

        public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
            => Task.FromResult(1);
    }

    private sealed class TestDbSet<T> : DbSet<T>, IQueryable<T>, IAsyncEnumerable<T> where T : class
    {
        private readonly List<T> _list;
        private readonly IQueryable<T> _queryable;

        public TestDbSet(List<T> list)
        {
            _list = list;
            _queryable = new TestAsyncEnumerable<T>(list.AsQueryable());
        }

        public override IEntityType EntityType => null!;

        public override EntityEntry<T> Add(T entity)
        {
            _list.Add(entity);
            return null!;
        }

        public override EntityEntry<T> Remove(T entity)
        {
            _list.Remove(entity);
            return null!;
        }

        Type IQueryable.ElementType => typeof(T);
        Expression IQueryable.Expression => _queryable.Expression;
        IQueryProvider IQueryable.Provider => _queryable.Provider;

        IEnumerator<T> IEnumerable<T>.GetEnumerator() => _list.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => _list.GetEnumerator();

        IAsyncEnumerator<T> IAsyncEnumerable<T>.GetAsyncEnumerator(CancellationToken cancellationToken)
            => new TestAsyncEnumerator<T>(_list.GetEnumerator());
    }

    private sealed class TestAsyncEnumerable<T> : EnumerableQuery<T>, IAsyncEnumerable<T>, IQueryable<T>
    {
        public TestAsyncEnumerable(IEnumerable<T> enumerable) : base(enumerable) { }
        public TestAsyncEnumerable(Expression expression) : base(expression) { }

        IQueryProvider IQueryable.Provider => new TestAsyncQueryProvider<T>(this);

        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
            => new TestAsyncEnumerator<T>(this.AsEnumerable().GetEnumerator());
    }

    private sealed class TestAsyncQueryProvider<T> : IAsyncQueryProvider
    {
        private readonly IQueryProvider _inner;

        public TestAsyncQueryProvider(IQueryProvider inner) => _inner = inner;

        public IQueryable CreateQuery(Expression expression) => new TestAsyncEnumerable<T>(expression);

        public IQueryable<TElement> CreateQuery<TElement>(Expression expression)
            => new TestAsyncEnumerable<TElement>(expression);

        public object? Execute(Expression expression) => _inner.Execute(expression);

        public TResult Execute<TResult>(Expression expression) => _inner.Execute<TResult>(expression);

        public TResult ExecuteAsync<TResult>(Expression expression, CancellationToken cancellationToken = default)
        {
            var expectedResultType = typeof(TResult);
            if (expectedResultType.IsGenericType && expectedResultType.GetGenericTypeDefinition() == typeof(Task<>))
            {
                var itemType = expectedResultType.GetGenericArguments()[0];
                var executeMethod = typeof(IQueryProvider)
                    .GetMethods()
                    .First(m => m.Name == nameof(IQueryProvider.Execute) && m.IsGenericMethod)
                    .MakeGenericMethod(itemType);

                var result = executeMethod.Invoke(_inner, new object[] { expression });
                var fromResultMethod = typeof(Task)
                    .GetMethod(nameof(Task.FromResult))!
                    .MakeGenericMethod(itemType);

                return (TResult)fromResultMethod.Invoke(null, new[] { result })!;
            }

            return _inner.Execute<TResult>(expression);
        }
    }

    private sealed class TestAsyncEnumerator<T> : IAsyncEnumerator<T>
    {
        private readonly IEnumerator<T> _inner;

        public TestAsyncEnumerator(IEnumerator<T> inner) => _inner = inner;

        public T Current => _inner.Current;

        public ValueTask DisposeAsync()
        {
            _inner.Dispose();
            return ValueTask.CompletedTask;
        }

        public ValueTask<bool> MoveNextAsync() => ValueTask.FromResult(_inner.MoveNext());
    }
}
