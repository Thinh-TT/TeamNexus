using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TeamNexus.Api.Tests.Infrastructure;
using TeamNexus.Modules.Ai.Services;
using TeamNexus.Modules.Ai.Services.Knowledge;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Api.Tests.Integration;

/// <summary>
/// Giai đoạn 14.7 Track B §B3 — the workspace knowledge base: the HTTP surface, the Accountability Layer
/// write path, the embedding pass and the pgvector retrieval.
/// <para>
/// <b>Every write goes through an approval.</b> The suite therefore asserts the shape of the request
/// (<b>201 with a Pending action, never the document</b>) as carefully as the shape of the applied result:
/// answering with a document at request time would tell the client the upload succeeded while a Manager still
/// had to approve it — and would mean the AI gateway had been bypassed.
/// </para>
/// </summary>
public sealed class Phase147KnowledgeTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _database;

    public Phase147KnowledgeTests(DatabaseFixture database)
    {
        _database = database;
    }

    // ---- reads --------------------------------------------------------------

    [Fact]
    public async Task Get_WhileRagIsOff_ReportsDisabledWithAnEmptyList()
    {
        // ⛔ The honesty rule: an empty list without the flag would tell the caller the workspace has no
        // documents, while the truth is that the AI is not reading any of them.
        await using var scenario = await _database.CreateScenarioAsync();
        var (admin, workspace, _) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(admin);

        var response = await client.GetAsync($"/api/workspaces/{workspace.Id}/knowledge/documents");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.False(document.RootElement.GetProperty("enabled").GetBoolean());
        Assert.Empty(document.RootElement.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Get_OnADocumentHeavyWorkspace_StillReportsDisabledWhileRagIsOff()
    {
        // The rows exist (an approved document from before the switch was flipped off) but must NOT be listed:
        // showing them would imply the AI can retrieve them.
        await using var scenario = await _database.CreateScenarioAsync();
        var (admin, workspace, _) = await SeedAsync(scenario);

        await InsertDocumentAsync(scenario, workspace.Id, admin.Id, "Tài liệu cũ", "nội dung", KnowledgeDocumentStatus.Ready);

        using var client = await scenario.AsUserAsync(admin);
        var response = await client.GetAsync($"/api/workspaces/{workspace.Id}/knowledge/documents");

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.False(document.RootElement.GetProperty("enabled").GetBoolean());
        Assert.Empty(document.RootElement.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Get_WithRagOn_ListsTheDocumentsOfThisWorkspaceOnly()
    {
        await using var scenario = await _database.CreateScenarioAsync(ragEnabled: true);
        var (admin, workspace, otherWorkspace) = await SeedAsync(scenario);

        await InsertDocumentAsync(scenario, workspace.Id, admin.Id, "Tài liệu A", "nội dung A", KnowledgeDocumentStatus.Ready);
        await InsertDocumentAsync(scenario, otherWorkspace.Id, admin.Id, "Tài liệu B", "nội dung B", KnowledgeDocumentStatus.Ready);

        using var client = await scenario.AsUserAsync(admin);

        var response = await client.GetAsync($"/api/workspaces/{workspace.Id}/knowledge/documents");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.True(document.RootElement.GetProperty("enabled").GetBoolean());

        var items = document.RootElement.GetProperty("items").EnumerateArray().ToList();

        var item = Assert.Single(items);
        Assert.Equal("Tài liệu A", item.GetProperty("title").GetString());
        Assert.Equal("ready", item.GetProperty("status").GetString());

        // The DTO carries no content: shipping every document's text to render a table would make this the most
        // expensive endpoint in the product.
        Assert.False(item.TryGetProperty("content", out _));
    }

    [Fact]
    public async Task Get_WithRagOn_HidesDeletedDocuments()
    {
        await using var scenario = await _database.CreateScenarioAsync(ragEnabled: true);
        var (admin, workspace, _) = await SeedAsync(scenario);

        await InsertDocumentAsync(scenario, workspace.Id, admin.Id, "Còn dùng", "x", KnowledgeDocumentStatus.Ready);
        await InsertDocumentAsync(scenario, workspace.Id, admin.Id, "Đã xoá", "y", KnowledgeDocumentStatus.Deleted);

        using var client = await scenario.AsUserAsync(admin);
        var response = await client.GetAsync($"/api/workspaces/{workspace.Id}/knowledge/documents");

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var items = document.RootElement.GetProperty("items").EnumerateArray().ToList();

        Assert.Single(items);
        Assert.Equal("Còn dùng", items[0].GetProperty("title").GetString());
    }

    [Fact]
    public async Task Get_ByAPlainMember_IsAllowedBecauseReadingIsMemberPlus()
    {
        await using var scenario = await _database.CreateScenarioAsync(ragEnabled: true);
        var (_, workspace, _) = await SeedAsync(scenario);
        var member = await scenario.CreateUserAsync("Thành viên");
        await AddMemberAsync(scenario, workspace.Id, member.Id, WorkspaceRole.Member);

        using var client = await scenario.AsUserAsync(member);

        var response = await client.GetAsync($"/api/workspaces/{workspace.Id}/knowledge/documents");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Get_ByAnOutsider_IsNotFound()
    {
        await using var scenario = await _database.CreateScenarioAsync(ragEnabled: true);
        var (_, workspace, _) = await SeedAsync(scenario);
        var outsider = await scenario.CreateUserAsync("Người ngoài");

        using var client = await scenario.AsUserAsync(outsider);

        var response = await client.GetAsync($"/api/workspaces/{workspace.Id}/knowledge/documents");

        // 404 rather than 403: the endpoint must not confirm that this workspace exists.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---- writes: permissions and the switch ---------------------------------

    [Fact]
    public async Task Post_ByAPlainMember_IsForbidden()
    {
        // Changing what the AI can read is workspace configuration — the same gate as taxonomies and the board
        // template.
        await using var scenario = await _database.CreateScenarioAsync(ragEnabled: true);
        var (_, workspace, _) = await SeedAsync(scenario);
        var member = await scenario.CreateUserAsync("Thành viên");
        await AddMemberAsync(scenario, workspace.Id, member.Id, WorkspaceRole.Member);

        using var client = await scenario.AsUserAsync(member);

        var response = await client.PostJsonAsync(
            $"/api/workspaces/{workspace.Id}/knowledge/documents",
            new { title = "Tài liệu", content = "nội dung", sourceUrl = (string?)null });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, await scenario.CountAsync<WorkspaceKnowledgeDocument>());
    }

    [Fact]
    public async Task Delete_ByAPlainMember_IsForbidden()
    {
        await using var scenario = await _database.CreateScenarioAsync(ragEnabled: true);
        var (admin, workspace, _) = await SeedAsync(scenario);
        var document = await InsertDocumentAsync(
            scenario, workspace.Id, admin.Id, "Tài liệu", "x", KnowledgeDocumentStatus.Ready);

        var member = await scenario.CreateUserAsync("Thành viên");
        await AddMemberAsync(scenario, workspace.Id, member.Id, WorkspaceRole.Member);

        using var client = await scenario.AsUserAsync(member);

        var response = await client.DeleteAsync(
            $"/api/workspaces/{workspace.Id}/knowledge/documents/{document}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_WhileRagIsOff_IsBadRequest()
    {
        // ⛔ Refused, not accepted-and-ignored: a document added while RAG is off would sit in the list looking
        // indexed while nothing could retrieve it.
        await using var scenario = await _database.CreateScenarioAsync();
        var (admin, workspace, _) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(admin);

        var response = await client.PostJsonAsync(
            $"/api/workspaces/{workspace.Id}/knowledge/documents",
            new { title = "Tài liệu", content = "nội dung khá dài", sourceUrl = (string?)null });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // Nothing was written — not even a Pending action for a document that could never be applied.
        Assert.Equal(0, await scenario.CountAsync<WorkspaceKnowledgeDocument>());
        Assert.Equal(0, await scenario.CountAsync<AiActionLog>(a => a.Action == AiActionTypes.CreateKnowledgeDocument));
    }

    [Fact]
    public async Task Post_WithValidInput_Returns201WithAPendingActionAndWritesNoDocument()
    {
        // ⛔ The whole point of the Accountability Layer here: the caller receives a PROPOSAL. Returning the
        // document would tell the client the upload succeeded when nothing has been indexed yet.
        await using var scenario = await _database.CreateScenarioAsync(ragEnabled: true);
        var (admin, workspace, _) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(admin);

        var response = await client.PostJsonAsync(
            $"/api/workspaces/{workspace.Id}/knowledge/documents",
            new { title = "Quy ước dự án", content = "Dùng snake_case.", sourceUrl = "https://example.test/doc" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var log = await response.Content.ReadFromJsonAsync<ActionDto>();
        Assert.NotNull(log);
        Assert.Equal(AiActionTypes.CreateKnowledgeDocument, log!.Action);
        Assert.Equal(AiEntityTypes.Workspace, log.EntityType);
        Assert.Equal("Pending", log.Status);

        // Nothing applied yet — there is no document to read, which is what makes "reject" meaningful.
        Assert.Equal(0, await scenario.CountAsync<WorkspaceKnowledgeDocument>());
        Assert.Equal(0, await scenario.CountAsync<WorkspaceKnowledgeChunk>());
    }

    // ---- validation ---------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Post_WithABlankTitle_IsBadRequest(string? title)
    {
        await using var scenario = await _database.CreateScenarioAsync(ragEnabled: true);
        var (admin, workspace, _) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(admin);

        var response = await client.PostJsonAsync(
            $"/api/workspaces/{workspace.Id}/knowledge/documents",
            new { title, content = "nội dung", sourceUrl = (string?)null });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Post_WithATitleLongerThanThreeHundred_IsBadRequest()
    {
        await using var scenario = await _database.CreateScenarioAsync(ragEnabled: true);
        var (admin, workspace, _) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(admin);

        var response = await client.PostJsonAsync(
            $"/api/workspaces/{workspace.Id}/knowledge/documents",
            new { title = new string('t', 301), content = "nội dung", sourceUrl = (string?)null });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // The boundary itself is accepted — the check is "longer than", not "as long as".
        var atLimit = await client.PostJsonAsync(
            $"/api/workspaces/{workspace.Id}/knowledge/documents",
            new { title = new string('t', 300), content = "nội dung", sourceUrl = (string?)null });

        Assert.Equal(HttpStatusCode.Created, atLimit.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n\t ")]
    public async Task Post_WithBlankContent_IsBadRequest(string? content)
    {
        await using var scenario = await _database.CreateScenarioAsync(ragEnabled: true);
        var (admin, workspace, _) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(admin);

        var response = await client.PostJsonAsync(
            $"/api/workspaces/{workspace.Id}/knowledge/documents",
            new { title = "Tài liệu", content, sourceUrl = (string?)null });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Post_WithASourceUrlLongerThanOneThousand_IsBadRequest()
    {
        await using var scenario = await _database.CreateScenarioAsync(ragEnabled: true);
        var (admin, workspace, _) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(admin);

        var response = await client.PostJsonAsync(
            $"/api/workspaces/{workspace.Id}/knowledge/documents",
            new { title = "Tài liệu", content = "nội dung", sourceUrl = "https://example.test/" + new string('u', 1_000) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Post_WithContentOverTheCeiling_IsBadRequestWithoutWritingAPendingAction()
    {
        // 400 000 chars is the applier's own ceiling; refusing at the door keeps a Pending action for a document
        // that could never be applied out of the history.
        await using var scenario = await _database.CreateScenarioAsync(ragEnabled: true);
        var (admin, workspace, _) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(admin);

        var response = await client.PostJsonAsync(
            $"/api/workspaces/{workspace.Id}/knowledge/documents",
            new { title = "Tài liệu", content = new string('c', 400_001), sourceUrl = (string?)null });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await scenario.CountAsync<AiActionLog>(a => a.Action == AiActionTypes.CreateKnowledgeDocument));
    }

    [Fact]
    public async Task Post_ByAnOutsider_IsNotFound()
    {
        await using var scenario = await _database.CreateScenarioAsync(ragEnabled: true);
        var (_, workspace, _) = await SeedAsync(scenario);
        var outsider = await scenario.CreateUserAsync("Người ngoài");

        using var client = await scenario.AsUserAsync(outsider);

        var response = await client.PostJsonAsync(
            $"/api/workspaces/{workspace.Id}/knowledge/documents",
            new { title = "Tài liệu", content = "nội dung", sourceUrl = (string?)null });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---- approve / undo -----------------------------------------------------

    [Fact]
    public async Task Approve_WritesTheDocumentAndItsChunksInOneGoAndUndoSoftDeletesIt()
    {
        await using var scenario = await _database.CreateScenarioAsync(ragEnabled: true);
        var (admin, workspace, _) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(admin);

        // Two paragraphs, so the chunk count is observable rather than trivially one.
        var content = "Quy ước đặt tên nhánh.\n\nQuy trình phát hành bản mới.";

        var created = await client.PostJsonAsync(
            $"/api/workspaces/{workspace.Id}/knowledge/documents",
            new { title = "Quy ước", content, sourceUrl = (string?)null });

        var log = await created.Content.ReadFromJsonAsync<ActionDto>();

        var approve = await client.PostJsonAsync($"/api/ai-actions/{log!.Id}/approve", new { });
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);

        await using (var db = scenario.NewDbContext())
        {
            var document = await db.WorkspaceKnowledgeDocuments.SingleAsync(d => d.WorkspaceId == workspace.Id);

            Assert.Equal("Quy ước", document.Title);
            Assert.Equal(content, document.Content);

            // ⛔ `Pending`, never `Ready`: "ready" means "every chunk has an embedding", and none exist yet.
            // Claiming ready here would make retrieval silently return nothing for a document the UI shows as
            // indexed.
            Assert.Equal(KnowledgeDocumentStatus.Pending, document.Status);
            Assert.Equal(admin.Id, document.CreatedByUserId);
            Assert.Null(document.Error);

            var chunks = await db.WorkspaceKnowledgeChunks
                .Where(c => c.DocumentId == document.Id)
                .OrderBy(c => c.ChunkIndex)
                .ToListAsync();

            Assert.Equal(document.ChunkCount, chunks.Count);
            Assert.Equal(2, chunks.Count);

            // ⛔ The denormalised workspace id is what makes retrieval a single-table query; a wrong copy here
            // would make the document unreachable from its own workspace and visible from another.
            Assert.All(chunks, chunk => Assert.Equal(workspace.Id, chunk.WorkspaceId));
            Assert.All(chunks, chunk => Assert.Null(chunk.Embedding));
            Assert.Equal([0, 1], chunks.Select(c => c.ChunkIndex).ToList());
        }

        // Undo of a CREATION is a no-op, not a soft delete: nothing was deleted, so marking the document
        // `deleted` would be a state the user never asked for and could never undo again.
        var undo = await client.PostJsonAsync($"/api/ai-actions/{log.Id}/undo", new { });
        Assert.Equal(HttpStatusCode.OK, undo.StatusCode);

        await using (var db = scenario.NewDbContext())
        {
            var document = await db.WorkspaceKnowledgeDocuments.IgnoreQueryFilters()
                .SingleAsync(d => d.WorkspaceId == workspace.Id);

            Assert.Equal(KnowledgeDocumentStatus.Pending, document.Status);

            // The chunks survive, which is what makes a future re-approve cheap rather than a re-embed.
            Assert.Equal(2, await db.WorkspaceKnowledgeChunks.CountAsync(c => c.DocumentId == document.Id));
        }
    }

    [Fact]
    public async Task Reject_WritesNoDocumentAtAll()
    {
        await using var scenario = await _database.CreateScenarioAsync(ragEnabled: true);
        var (admin, workspace, _) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(admin);

        var created = await client.PostJsonAsync(
            $"/api/workspaces/{workspace.Id}/knowledge/documents",
            new { title = "Bị từ chối", content = "nội dung", sourceUrl = (string?)null });

        var log = await created.Content.ReadFromJsonAsync<ActionDto>();

        var reject = await client.PostJsonAsync(
            $"/api/ai-actions/{log!.Id}/reject", new { note = "Không cần" });

        Assert.Equal(HttpStatusCode.OK, reject.StatusCode);
        Assert.Equal(0, await scenario.CountAsync<WorkspaceKnowledgeDocument>());
        Assert.Equal(0, await scenario.CountAsync<WorkspaceKnowledgeChunk>());
    }

    [Fact]
    public async Task Delete_ViaTheApi_SoftDeletesOnlyAfterApproval()
    {
        await using var scenario = await _database.CreateScenarioAsync(ragEnabled: true);
        var (admin, workspace, _) = await SeedAsync(scenario);

        var documentId = await InsertDocumentAsync(
            scenario, workspace.Id, admin.Id, "Tài liệu cũ", "nội dung", KnowledgeDocumentStatus.Ready);

        using var client = await scenario.AsUserAsync(admin);

        var response = await client.DeleteAsync(
            $"/api/workspaces/{workspace.Id}/knowledge/documents/{documentId}");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var log = await response.Content.ReadFromJsonAsync<ActionDto>();

        // Still there: the request only proposed the removal.
        await using (var db = scenario.NewDbContext())
        {
            var beforeApprove = await db.WorkspaceKnowledgeDocuments.SingleAsync(d => d.Id == documentId);
            Assert.Equal(KnowledgeDocumentStatus.Ready, beforeApprove.Status);
        }

        var approve = await client.PostJsonAsync($"/api/ai-actions/{log!.Id}/approve", new { });
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);

        await using (var db = scenario.NewDbContext())
        {
            var afterApprove = await db.WorkspaceKnowledgeDocuments.SingleAsync(d => d.Id == documentId);
            Assert.Equal(KnowledgeDocumentStatus.Deleted, afterApprove.Status);
        }
    }

    [Fact]
    public async Task Delete_OfAnAlreadyDeletedDocument_IsBadRequest()
    {
        await using var scenario = await _database.CreateScenarioAsync(ragEnabled: true);
        var (admin, workspace, _) = await SeedAsync(scenario);

        var documentId = await InsertDocumentAsync(
            scenario, workspace.Id, admin.Id, "Đã xoá", "nội dung", KnowledgeDocumentStatus.Deleted);

        using var client = await scenario.AsUserAsync(admin);

        var response = await client.DeleteAsync(
            $"/api/workspaces/{workspace.Id}/knowledge/documents/{documentId}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Delete_OfADocumentFromAnotherWorkspace_IsNotFound()
    {
        // The document id is untrusted input; the workspace comes from the route, so a caller cannot reach
        // across workspaces by naming an id.
        await using var scenario = await _database.CreateScenarioAsync(ragEnabled: true);
        var (admin, workspace, otherWorkspace) = await SeedAsync(scenario);

        var foreign = await InsertDocumentAsync(
            scenario, otherWorkspace.Id, admin.Id, "Của workspace khác", "x", KnowledgeDocumentStatus.Ready);

        using var client = await scenario.AsUserAsync(admin);

        var response = await client.DeleteAsync(
            $"/api/workspaces/{workspace.Id}/knowledge/documents/{foreign}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Approve_OfADeleteActionNamingAnotherWorkspaceDocument_DoesNotTouchIt()
    {
        // ⛔ The scope comes from the action's context, never from the payload. A hand-edited (or model-supplied)
        // document id belonging to another workspace must not be deletable through this workspace's action.
        await using var scenario = await _database.CreateScenarioAsync(ragEnabled: true);
        var (admin, workspace, otherWorkspace) = await SeedAsync(scenario);

        var foreign = await InsertDocumentAsync(
            scenario, otherWorkspace.Id, admin.Id, "Của workspace khác", "x", KnowledgeDocumentStatus.Ready);

        var actionId = await InsertDeleteActionAsync(scenario, workspace.Id, admin.Id, foreign);

        using var client = await scenario.AsUserAsync(admin);

        var approve = await client.PostJsonAsync($"/api/ai-actions/{actionId}/approve", new { });

        // The action cannot succeed (there is no such document in THIS workspace)…
        Assert.NotEqual(HttpStatusCode.OK, approve.StatusCode);

        // …and crucially the other workspace's document is untouched.
        await using var db = scenario.NewDbContext();
        var untouched = await db.WorkspaceKnowledgeDocuments.SingleAsync(d => d.Id == foreign);
        Assert.Equal(KnowledgeDocumentStatus.Ready, untouched.Status);
    }

    [Fact]
    public async Task Approve_OfADeleteActionForAMissingDocument_IsNotFound()
    {
        await using var scenario = await _database.CreateScenarioAsync(ragEnabled: true);
        var (admin, workspace, _) = await SeedAsync(scenario);

        var actionId = await InsertDeleteActionAsync(scenario, workspace.Id, admin.Id, Guid.NewGuid());

        using var client = await scenario.AsUserAsync(admin);

        var response = await client.PostJsonAsync($"/api/ai-actions/{actionId}/approve", new { });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Approve_OfACreateActionWithAnEmptyTitle_IsBadRequestNotA500()
    {
        // `after_snapshot` is untrusted (it round-tripped through the client's preview), so a hand-edited one
        // must produce a clean 400 rather than a deserialization stack trace.
        await using var scenario = await _database.CreateScenarioAsync(ragEnabled: true);
        var (admin, workspace, _) = await SeedAsync(scenario);

        var actionId = await InsertActionAsync(
            scenario, workspace.Id, admin.Id, """{"kind":null,"title":"   ","sourceUrl":null,"content":"nội dung"}""");

        using var client = await scenario.AsUserAsync(admin);

        var response = await client.PostJsonAsync($"/api/ai-actions/{actionId}/approve", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await scenario.CountAsync<WorkspaceKnowledgeDocument>());
    }

    [Fact]
    public async Task Approve_OfACreateActionWithMalformedJson_IsBadRequestNotA500()
    {
        // `after_snapshot` is a `jsonb` column, so what reaches the applier is always *valid* JSON — the
        // untrusted part is its SHAPE. A bare JSON string is the shape a client (or a model) actually produces
        // when it sends the wrong thing, and it must be a clean 400 rather than a deserialization stack trace.
        await using var scenario = await _database.CreateScenarioAsync(ragEnabled: true);
        var (admin, workspace, _) = await SeedAsync(scenario);

        var actionId = await InsertActionAsync(
            scenario, workspace.Id, admin.Id, "\"đây không phải là object\"");

        using var client = await scenario.AsUserAsync(admin);

        var response = await client.PostJsonAsync($"/api/ai-actions/{actionId}/approve", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Approve_OfACreateActionWithNoSnapshotAtAll_IsBadRequest()
    {
        await using var scenario = await _database.CreateScenarioAsync(ragEnabled: true);
        var (admin, workspace, _) = await SeedAsync(scenario);

        var actionId = await InsertActionAsync(scenario, workspace.Id, admin.Id, null);

        using var client = await scenario.AsUserAsync(admin);

        var response = await client.PostJsonAsync($"/api/ai-actions/{actionId}/approve", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---- undo of a delete ---------------------------------------------------

    [Fact]
    public async Task Undo_OfADeletion_RestoresPendingWhenChunksHaveNoEmbeddingYet()
    {
        // ⛔ `ready` only when every chunk already carries a vector; otherwise the document would claim an
        // embedding it does not have and retrieval would rank a NULL.
        await using var scenario = await _database.CreateScenarioAsync(ragEnabled: true);
        var (admin, workspace, _) = await SeedAsync(scenario);

        var documentId = await InsertDocumentAsync(
            scenario, workspace.Id, admin.Id, "Tài liệu", "nội dung", KnowledgeDocumentStatus.Ready, withChunks: true);

        using var client = await scenario.AsUserAsync(admin);

        var delete = await client.DeleteAsync(
            $"/api/workspaces/{workspace.Id}/knowledge/documents/{documentId}");
        var log = await delete.Content.ReadFromJsonAsync<ActionDto>();

        Assert.Equal(HttpStatusCode.OK, (await client.PostJsonAsync($"/api/ai-actions/{log!.Id}/approve", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostJsonAsync($"/api/ai-actions/{log.Id}/undo", new { })).StatusCode);

        await using var db = scenario.NewDbContext();
        var restored = await db.WorkspaceKnowledgeDocuments.SingleAsync(d => d.Id == documentId);

        Assert.Equal(KnowledgeDocumentStatus.Pending, restored.Status);
    }

    // ---- the embedding pass -------------------------------------------------

    [Fact]
    public async Task EmbeddingRunner_WithEmbeddingsOff_IsANoOp()
    {
        // `Embeddings:Enabled=false` must cost nothing: the chunks stay pending and no provider is consulted.
        await using var scenario = await _database.CreateScenarioAsync(ragEnabled: true);
        var (admin, workspace, _) = await SeedAsync(scenario);

        var documentId = await InsertDocumentAsync(
            scenario, workspace.Id, admin.Id, "Tài liệu", "Nội dung cần nhúng.", KnowledgeDocumentStatus.Pending, withChunks: true);

        var result = await RunEmbeddingAsync(scenario);

        Assert.True(result.Skipped);
        Assert.Equal(0, result.Embedded);

        await using var db = scenario.NewDbContext();
        Assert.Equal(KnowledgeDocumentStatus.Pending, (await db.WorkspaceKnowledgeDocuments.SingleAsync(d => d.Id == documentId)).Status);
        Assert.Equal(0, await db.WorkspaceKnowledgeChunks.CountAsync(c => c.Embedding != null));
    }

    [Fact]
    public async Task EmbeddingRunner_WithTheOfflineProvider_EmbedsEveryChunkAndPromotesTheDocument()
    {
        await using var scenario = await _database.CreateScenarioAsync(ragEnabled: true, embeddingsEnabled: true);
        var (admin, workspace, _) = await SeedAsync(scenario);

        var documentId = await InsertDocumentAsync(
            scenario, workspace.Id, admin.Id, "Tài liệu", "Đoạn một.\n\nĐoạn hai.", KnowledgeDocumentStatus.Pending, withChunks: true);

        var result = await RunEmbeddingAsync(scenario);
        Assert.Equal(2, result.Embedded);
        Assert.Equal(1, result.DocumentsPromoted);
        Assert.Equal(0, result.DocumentsFailed);
        Assert.False(result.Skipped);

        await using var db = scenario.NewDbContext();

        var chunks = await db.WorkspaceKnowledgeChunks.Where(c => c.DocumentId == documentId).ToListAsync();

        Assert.All(chunks, chunk => Assert.NotNull(chunk.Embedding));

        // ⛔ `ready` is the flag that admits the document into retrieval, so it may only be set once "no chunk is
        // missing a vector" is actually true.
        Assert.Equal(KnowledgeDocumentStatus.Ready, (await db.WorkspaceKnowledgeDocuments.SingleAsync(d => d.Id == documentId)).Status);
    }

    [Fact]
    public async Task EmbeddingRunner_IsIdempotentSoASecondPassDoesNoWork()
    {
        await using var scenario = await _database.CreateScenarioAsync(ragEnabled: true, embeddingsEnabled: true);
        var (admin, workspace, _) = await SeedAsync(scenario);

        await InsertDocumentAsync(
            scenario, workspace.Id, admin.Id, "Tài liệu", "Đoạn một.", KnowledgeDocumentStatus.Pending, withChunks: true);

        Assert.Equal(1, (await RunEmbeddingAsync(scenario)).Embedded);

        var second = await RunEmbeddingAsync(scenario);

        // Nothing left to do, and no second embedding paid for.
        Assert.True(second.Skipped);
        Assert.Equal(0, second.Embedded);
        Assert.Equal(0, second.DocumentsPromoted);
    }

    [Fact]
    public async Task EmbeddingRunner_WithAProviderReturningTheWrongDimension_MarksTheDocumentFailedWithAReason()
    {
        // ⛔ A wrong-length vector must never reach a `vector(1536)` column: the insert would fail with a message
        // that names no document. The runner has to catch it and record why.
        var provider = new WrongDimensionEmbeddingProvider();

        await using var scenario = await _database.CreateScenarioAsync(
            ragEnabled: true, embeddingsEnabled: true, embeddingProvider: provider);

        var (admin, workspace, _) = await SeedAsync(scenario);

        var documentId = await InsertDocumentAsync(
            scenario, workspace.Id, admin.Id, "Tài liệu", "Đoạn một.", KnowledgeDocumentStatus.Pending, withChunks: true);

        var result = await RunEmbeddingAsync(scenario);

        Assert.Equal(1, result.DocumentsFailed);
        Assert.Equal(0, result.Embedded);

        await using var db = scenario.NewDbContext();
        var document = await db.WorkspaceKnowledgeDocuments.SingleAsync(d => d.Id == documentId);

        Assert.Equal(KnowledgeDocumentStatus.Failed, document.Status);
        Assert.False(string.IsNullOrWhiteSpace(document.Error));

        // The reason names the mismatch, so an operator can act on it.
        Assert.Contains("chiều", document.Error!, StringComparison.Ordinal);
        Assert.Equal(0, await db.WorkspaceKnowledgeChunks.CountAsync(c => c.Embedding != null));
    }

    [Fact]
    public async Task EmbeddingRunner_WithAThrowingProvider_MarksTheDocumentFailedWithTheProviderReason()
    {
        var provider = new ThrowingEmbeddingProvider();

        await using var scenario = await _database.CreateScenarioAsync(
            ragEnabled: true, embeddingsEnabled: true, embeddingProvider: provider);

        var (admin, workspace, _) = await SeedAsync(scenario);

        var documentId = await InsertDocumentAsync(
            scenario, workspace.Id, admin.Id, "Tài liệu", "Đoạn một.", KnowledgeDocumentStatus.Pending, withChunks: true);

        var result = await RunEmbeddingAsync(scenario);

        Assert.Equal(1, result.DocumentsFailed);

        await using var db = scenario.NewDbContext();
        var document = await db.WorkspaceKnowledgeDocuments.SingleAsync(d => d.Id == documentId);

        // A document that sits silently in `pending` forever is the failure this replaces.
        Assert.Equal(KnowledgeDocumentStatus.Failed, document.Status);
        Assert.Contains("nhà cung cấp", document.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EmbeddingRunner_IgnoresChunksOfSoftDeletedDocuments()
    {
        // A deleted document's chunks keep their vectors so a re-approve is cheap, but they must not consume
        // embedding calls.
        await using var scenario = await _database.CreateScenarioAsync(ragEnabled: true, embeddingsEnabled: true);
        var (admin, workspace, _) = await SeedAsync(scenario);

        await InsertDocumentAsync(
            scenario, workspace.Id, admin.Id, "Đã xoá", "Đoạn một.", KnowledgeDocumentStatus.Deleted, withChunks: true);

        var result = await RunEmbeddingAsync(scenario);

        Assert.True(result.Skipped);
        Assert.Equal(0, result.Embedded);
    }

    // ---- retrieval ----------------------------------------------------------

    [Fact]
    public async Task GetTopChunks_ReturnsTheNearestReadyChunkOfThisWorkspaceOnly()
    {
        // ⛔ The leak this catches: retrieval that forgot the workspace predicate would hand one workspace's
        // documents to another workspace's chat prompt.
        await using var scenario = await _database.CreateScenarioAsync(ragEnabled: true, embeddingsEnabled: true);
        var (admin, workspace, otherWorkspace) = await SeedAsync(scenario);

        var mine = await InsertDocumentAsync(
            scenario, workspace.Id, admin.Id, "Tài liệu của tôi", "Nội dung riêng tư của workspace này.",
            KnowledgeDocumentStatus.Pending, withChunks: true);

        var theirs = await InsertDocumentAsync(
            scenario, otherWorkspace.Id, admin.Id, "Tài liệu workspace khác", "Nội dung của workspace khác.",
            KnowledgeDocumentStatus.Pending, withChunks: true);

        // Embed both, then promote both so `onlyReady` does not decide the outcome.
        await RunEmbeddingAsync(scenario);
        await PromoteAsync(scenario, mine);
        await PromoteAsync(scenario, theirs);

        var query = await EmbedAsync(scenario, "Nội dung riêng tư của workspace này.");

        var chunks = await QueryAsync(scenario, workspace.Id, query);

        var chunk = Assert.Single(chunks);
        Assert.Equal("Tài liệu của tôi", chunk.DocumentTitle);
        Assert.DoesNotContain("workspace khác", chunk.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetTopChunks_ExcludesDocumentsThatAreNotReady()
    {
        // ⛔ The `onlyReady` predicate is what keeps a half-indexed document out of the prompt. Set up directly
        // (rather than through the runner, which would PROMOTE the document to ready) so the chunk genuinely has
        // a vector while its document is still `pending` — the only state in which the predicate can be observed.
        await using var scenario = await _database.CreateScenarioAsync(ragEnabled: true, embeddingsEnabled: true);
        var (admin, workspace, _) = await SeedAsync(scenario);

        var documentId = await InsertDocumentAsync(
            scenario, workspace.Id, admin.Id, "Chưa sẵn sàng", "Nội dung.",
            KnowledgeDocumentStatus.Pending, withChunks: true);

        var query = await EmbedAsync(scenario, "Nội dung.");

        await SetChunkVectorsAsync(scenario, documentId, query);

        // The document stayed pending, so `onlyReady` must hide it…
        Assert.Empty(await QueryAsync(scenario, workspace.Id, query, onlyReady: true));

        // …while the same row IS reachable when the caller asks for the wider view (which is what the embedding
        // runner needs). Without this half, an empty result could simply mean the fixture never wrote anything.
        var withoutFilter = await QueryAsync(scenario, workspace.Id, query, onlyReady: false);

        var chunk = Assert.Single(withoutFilter);
        Assert.Equal("Chưa sẵn sàng", chunk.DocumentTitle);
    }

    [Fact]
    public async Task GetTopChunks_ReturnsAtMostOneChunkPerDocument()
    {
        // A single long document would otherwise fill the whole result set from one source and the model would
        // lose all variety.
        await using var scenario = await _database.CreateScenarioAsync(ragEnabled: true, embeddingsEnabled: true);
        var (admin, workspace, _) = await SeedAsync(scenario);

        var document = await InsertDocumentAsync(
            scenario, workspace.Id, admin.Id, "Tài liệu dài",
            "Đoạn một.\n\nĐoạn hai.\n\nĐoạn ba.", KnowledgeDocumentStatus.Pending, withChunks: true);

        Assert.Equal(3, await scenario.CountAsync<WorkspaceKnowledgeChunk>(c => c.DocumentId == document));

        await RunEmbeddingAsync(scenario);
        await PromoteAsync(scenario, document);

        var query = await EmbedAsync(scenario, "Đoạn hai.");
        var chunks = await QueryAsync(scenario, workspace.Id, query, maxChunks: 5, maxDocuments: 5);

        Assert.Single(chunks);
    }

    [Fact]
    public async Task GetTopChunks_ForAnUnknownWorkspace_ReturnsEmptyInsteadOfThrowing()
    {
        // "There is no knowledge base here" and "the knowledge base has nothing" are the same answer to the
        // caller; throwing would turn a prompt enhancement into a broken chat.
        await using var scenario = await _database.CreateScenarioAsync(ragEnabled: true, embeddingsEnabled: true);

        var query = await EmbedAsync(scenario, "bất kỳ");

        var chunks = await QueryAsync(scenario, Guid.NewGuid(), query);

        Assert.Empty(chunks);
    }

    [Fact]
    public async Task GetTopChunks_WithNoUsableQuery_ReturnsEmptyInsteadOfThrowing()
    {
        await using var scenario = await _database.CreateScenarioAsync(ragEnabled: true, embeddingsEnabled: true);
        var (admin, workspace, _) = await SeedAsync(scenario);

        await InsertDocumentAsync(
            scenario, workspace.Id, admin.Id, "Tài liệu", "Nội dung.", KnowledgeDocumentStatus.Ready, withChunks: true);

        await using var scope = scenario.Services.CreateAsyncScope();
        var store = scope.ServiceProvider
            .GetRequiredService<TeamNexus.Modules.Board.Services.IWorkspaceKnowledgeStore>();

        Assert.Empty(await store.GetTopChunksAsync(workspace.Id, ReadOnlyMemory<float>.Empty, true, 5, 5));
        Assert.Empty(await store.GetTopChunksAsync(Guid.Empty, new float[3], true, 5, 5));
        Assert.Empty(await store.GetTopChunksAsync(workspace.Id, new float[3], true, 5, 0));
    }

    // ---- schema facts -------------------------------------------------------

    [Fact]
    public async Task Schema_HasTheVectorExtensionAndTheTwoKnowledgeTables()
    {
        // ⛔ Asserted from code rather than by shelling out to `dotnet ef`: an image without pgvector cannot run
        // the migration at all, and `DatabaseFixture` already turns that into a skip with an actionable message.
        await using var scenario = await _database.CreateScenarioAsync();
        await using var db = scenario.NewDbContext();

        var connection = db.Database.GetDbConnection();
        var wasClosed = connection.State != System.Data.ConnectionState.Open;

        if (wasClosed)
        {
            await connection.OpenAsync();
        }

        try
        {
            Assert.True(await ScalarBoolAsync(connection,
                "select exists (select 1 from pg_extension where extname = 'vector')"));

            Assert.True(await ScalarBoolAsync(connection,
                "select exists (select 1 from information_schema.tables "
                + "where table_schema = 'public' and table_name = 'workspace_knowledge_documents')"));

            Assert.True(await ScalarBoolAsync(connection,
                "select exists (select 1 from information_schema.tables "
                + "where table_schema = 'public' and table_name = 'workspace_knowledge_chunks')"));

            // The column dimension is part of the column TYPE, which is what makes a model/dimension mismatch a
            // hard failure instead of a silent one.
            Assert.Equal("vector(1536)", await ScalarStringAsync(connection,
                "select format_type(a.atttypid, a.atttypmod) from pg_attribute a "
                + "join pg_class c on c.oid = a.attrelid "
                + "where c.relname = 'workspace_knowledge_chunks' and a.attname = 'embedding'"));

            // The retrieval index must use the cosine operator class, or a cosine ORDER BY simply will not use it.
            Assert.Contains("vector_cosine_ops", await ScalarStringAsync(connection,
                "select am.amname || ':' || opc.opcname from pg_index i "
                + "join pg_class c on c.oid = i.indexrelid "
                + "join pg_am am on am.oid = c.relam "
                + "join pg_opclass opc on opc.oid = i.indclass[0] "
                + "where i.indexrelid = (select oid from pg_class where relname = 'ix_workspace_knowledge_chunks_embedding')"));
        }
        finally
        {
            if (wasClosed)
            {
                await connection.CloseAsync();
            }
        }
    }

    [Fact]
    public async Task Schema_MigrationChainEndsWithTheTwoTrackBMigrations()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        await using var db = scenario.NewDbContext();

        var connection = db.Database.GetDbConnection();
        var wasClosed = connection.State != System.Data.ConnectionState.Open;

        if (wasClosed)
        {
            await connection.OpenAsync();
        }

        try
        {
            var count = await ScalarLongAsync(connection,
                "select count(*) from \"__EFMigrationsHistory\"");

            // Fourteen applied migrations, and the last two are this phase's — asserted from the history table
            // rather than by shelling out to `dotnet ef migrations list`.
            Assert.Equal(14, count);

            // Read as one delimited scalar rather than a multi-row reader: `ExecuteScalar` is the shape every
            // other probe in this suite uses, and it keeps the assertion a single string comparison.
            //
            // ⚠️ The column is `migration_id`, not `MigrationId`: the whole schema goes through EF's snake_case
            // naming convention, and `count(*)` is the only probe in this method that would not have revealed it.
            var lastTwo = await ScalarStringAsync(connection,
                "select string_agg(\"migration_id\", '|' order by \"migration_id\" desc) "
                + "from (select \"migration_id\" from \"__EFMigrationsHistory\" order by \"migration_id\" desc limit 2) t");

            Assert.Equal("20261015122250_Phase147KnowledgeBase|20261015122214_Phase147AgentPersonas", lastTwo);

            // The persona index is the race guard for "one agent row per persona"; it replaced the Phase 7
            // single-agent index, and it is built on COALESCE(persona_key,'general') because PostgreSQL treats
            // every NULL as distinct.
            var personaIndex = await ScalarStringAsync(connection,
                "select indexdef from pg_indexes where tablename = 'workspace_members' "
                + "and indexname = 'uq_workspace_members_ai_persona'");

            Assert.NotNull(personaIndex);
            Assert.Contains("COALESCE", personaIndex!, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("ai_agent", personaIndex!, StringComparison.OrdinalIgnoreCase);

            // The Phase 7 index is gone, so "exactly one agent per workspace" is no longer the rule — which is
            // the whole point of Track B.
            Assert.False(await ScalarBoolAsync(connection,
                "select exists (select 1 from pg_indexes where tablename = 'workspace_members' "
                + "and indexname = 'uq_workspace_members_ai_agent')"));
        }
        finally
        {
            if (wasClosed)
            {
                await connection.CloseAsync();
            }
        }
    }

    // ---- helpers ------------------------------------------------------------

    private static async Task<(ApplicationUser Admin, Workspace Workspace, Workspace Other)> SeedAsync(
        TestScenario scenario)
    {
        var admin = await scenario.CreateUserAsync("Quản trị");
        var workspace = await scenario.CreateWorkspaceAsync(admin, "Workspace Kiến Thức");
        var other = await scenario.CreateWorkspaceAsync(admin, "Workspace Khác");

        return (admin, workspace, other);
    }

    private static async Task AddMemberAsync(TestScenario scenario, Guid workspaceId, Guid userId, WorkspaceRole role)
    {
        await using var db = scenario.NewDbContext();

        db.WorkspaceMembers.Add(new WorkspaceMember
        {
            WorkspaceId = workspaceId,
            UserId = userId,
            Role = role,
            MemberType = MemberType.Human,
            JoinedAt = DateTimeOffset.UtcNow,
        });

        await db.SaveChangesAsync();
    }

    private static async Task<Guid> InsertDocumentAsync(
        TestScenario scenario,
        Guid workspaceId,
        Guid createdBy,
        string title,
        string content,
        KnowledgeDocumentStatus status,
        bool withChunks = false)
    {
        await using var db = scenario.NewDbContext();

        var document = new WorkspaceKnowledgeDocument
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            Title = title,
            Content = content,
            Status = status,
            ChunkCount = withChunks ? 1 : 0,
            CreatedByUserId = createdBy,
        };

        db.WorkspaceKnowledgeDocuments.Add(document);

        if (withChunks)
        {
            var index = 0;

            foreach (var piece in content.Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
            {
                db.WorkspaceKnowledgeChunks.Add(new WorkspaceKnowledgeChunk
                {
                    Id = Guid.NewGuid(),
                    DocumentId = document.Id,
                    WorkspaceId = workspaceId,
                    ChunkIndex = index++,
                    Content = piece.Trim(),
                });
            }

            document.ChunkCount = index;
        }

        await db.SaveChangesAsync();
        return document.Id;
    }

    private static async Task<Guid> InsertActionAsync(
        TestScenario scenario, Guid workspaceId, Guid requestedBy, string? afterSnapshot)
    {
        await using var db = scenario.NewDbContext();

        var log = new AiActionLog
        {
            Id = Guid.NewGuid(),
            Action = AiActionTypes.CreateKnowledgeDocument,
            EntityType = AiEntityTypes.Workspace,
            EntityId = workspaceId,
            Basis = "{}",
            AfterSnapshot = afterSnapshot,
            Status = AiActionStatus.Pending,
            RequestedByUserId = requestedBy,
        };

        db.AiActionLogs.Add(log);

        // The FK on `requested_by_user_id` points at `users`, and the caller already exists.
        await db.SaveChangesAsync();
        return log.Id;
    }

    private static Task<Guid> InsertDeleteActionAsync(
        TestScenario scenario, Guid workspaceId, Guid requestedBy, Guid documentId)
        => InsertActionAsync(
            scenario,
            workspaceId,
            requestedBy,
            JsonSerializer.Serialize(new { kind = "Delete", documentId, title = "x" }));

    private static async Task<KnowledgeEmbeddingRunResult> RunEmbeddingAsync(TestScenario scenario)
    {
        await using var scope = scenario.Services.CreateAsyncScope();
        var runner = scope.ServiceProvider.GetRequiredService<IKnowledgeEmbeddingRunner>();

        return await runner.RunOnceAsync();
    }

    private static async Task<ReadOnlyMemory<float>> EmbedAsync(TestScenario scenario, string text)
    {
        await using var scope = scenario.Services.CreateAsyncScope();
        var provider = scope.ServiceProvider.GetRequiredService<IEmbeddingProvider>();

        var vectors = await provider.EmbedAsync([text]);

        return vectors[0];
    }

    private static async Task<IReadOnlyList<TeamNexus.Modules.Board.Services.KnowledgeChunk>> QueryAsync(
        TestScenario scenario,
        Guid workspaceId,
        ReadOnlyMemory<float> query,
        int maxChunks = 5,
        int maxDocuments = 5,
        bool onlyReady = true)
    {
        await using var scope = scenario.Services.CreateAsyncScope();
        var store = scope.ServiceProvider
            .GetRequiredService<TeamNexus.Modules.Board.Services.IWorkspaceKnowledgeStore>();

        return await store.GetTopChunksAsync(workspaceId, query, onlyReady, maxDocuments, maxChunks);
    }

    /// <summary>Gives every chunk of a document the given vector without touching the document's status.</summary>
    private static async Task SetChunkVectorsAsync(
        TestScenario scenario, Guid documentId, ReadOnlyMemory<float> vector)
    {
        await using var db = scenario.NewDbContext();

        var chunks = await db.WorkspaceKnowledgeChunks.Where(c => c.DocumentId == documentId).ToListAsync();

        foreach (var chunk in chunks)
        {
            chunk.Embedding = new Pgvector.Vector(vector.ToArray());
        }

        await db.SaveChangesAsync();
    }

    /// <summary>Forces a document to `ready` without going through the runner (used to isolate retrieval).</summary>
    private static async Task PromoteAsync(TestScenario scenario, Guid documentId)
    {
        await using var db = scenario.NewDbContext();

        var document = await db.WorkspaceKnowledgeDocuments.SingleAsync(d => d.Id == documentId);
        document.Status = KnowledgeDocumentStatus.Ready;

        await db.SaveChangesAsync();
    }

    private static async Task<bool> ScalarBoolAsync(System.Data.Common.DbConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        return (bool)(await command.ExecuteScalarAsync() ?? false);
    }

    private static async Task<long> ScalarLongAsync(System.Data.Common.DbConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        return Convert.ToInt64(await command.ExecuteScalarAsync() ?? 0L, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<string?> ScalarStringAsync(System.Data.Common.DbConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        return await command.ExecuteScalarAsync() as string;
    }

    private sealed record ActionDto(Guid Id, string Action, string EntityType, Guid? EntityId, string Status);
}
