using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pgvector;
using TeamNexus.Api.Tests.Infrastructure;
using TeamNexus.Modules.Ai.DTOs;
using TeamNexus.Modules.Ai.Services;
using TeamNexus.Modules.Ai.Services.Jev;
using TeamNexus.Modules.Ai.Services.Knowledge;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Api.Tests.Integration;

/// <summary>
/// Giai đoạn 14.7 Track B §B3 — the knowledge block inside the board chat's system prompt.
/// <para>
/// <b>Asserted through <see cref="IAgentChatService.PrepareAsync"/>, not through a frame.</b> The prompt is an
/// input to the model and no SSE frame echoes it back, so an HTTP-only test could prove nothing about whether
/// the retrieved knowledge actually reached the context — or where in it the block landed.
/// </para>
/// <para>
/// Both switches have to agree (<c>Jev:RagEnabled</c> AND <c>Embeddings:Enabled</c>), which is why every case
/// here opts in to both explicitly. The default host must never inject a block an older suite never expected.
/// </para>
/// </summary>
public sealed class Phase147RagPromptTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _database;

    public Phase147RagPromptTests(DatabaseFixture database)
    {
        _database = database;
    }

    [Fact]
    public async Task Prepare_WithRagAndEmbeddingsOn_InjectsTheApprovedAndEmbeddedKnowledgeBeforeTheDigest()
    {
        await using var scenario = await _database.CreateScenarioAsync(ragEnabled: true, embeddingsEnabled: true);
        var seed = await SeedAsync(scenario);

        const string Content = "Mọi API phải trả về mã lỗi dạng văn bản có tiền tố ERR-.";

        await InsertAndEmbedAsync(scenario, seed, "Quy ước API", Content);

        // Nothing attached, so the digest is composed too — which is what makes the ORDER observable.
        var prepared = await PrepareAsync(scenario, seed, "Quy ước API của dự án là gì?");

        Assert.Contains(KnowledgeChunking.ContextHeader, prepared.SystemPrompt, StringComparison.Ordinal);
        Assert.Contains(Content, prepared.SystemPrompt, StringComparison.Ordinal);
        Assert.Contains("Quy ước API", prepared.SystemPrompt, StringComparison.Ordinal);

        // ⛔ Reference material goes BEFORE the board snapshot: the digest is "what the board looks like right
        // now" and the knowledge is "what the project has agreed", and a model that reads the plan after the
        // snapshot will describe the snapshot as the plan.
        var knowledgeAt = prepared.SystemPrompt.IndexOf(KnowledgeChunking.ContextHeader, StringComparison.Ordinal);
        var digestAt = prepared.SystemPrompt.IndexOf(
            AgentChatPrompts.DigestBlockMarker, StringComparison.Ordinal);

        Assert.True(digestAt >= 0, "Fixture không tạo được khối tổng quan bảng để so thứ tự.");
        Assert.True(
            knowledgeAt < digestAt,
            $"Khối kiến thức phải đứng TRƯỚC tổng quan bảng (kiến thức @{knowledgeAt}, tổng quan @{digestAt}).");
    }

    [Fact]
    public async Task Prepare_WithRagOnButNothingRetrievable_OmitsTheBlockEntirely()
    {
        // An empty workspace is the normal case for a fresh project, and a bare "KIẾN THỨC DỰ ÁN:" heading with
        // nothing under it would claim knowledge the model is not being shown.
        await using var scenario = await _database.CreateScenarioAsync(ragEnabled: true, embeddingsEnabled: true);
        var seed = await SeedAsync(scenario);

        var prepared = await PrepareAsync(scenario, seed, "Dự án có quy ước gì?");

        Assert.DoesNotContain(KnowledgeChunking.ContextHeader, prepared.SystemPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Prepare_WithRagOff_OmitsTheBlockEvenWhenAReadyDocumentExists()
    {
        // ⛔ The switch is what decides: the document exists and is embedded, and it must STILL not reach the
        // prompt. A regression that ignored `RagEnabled` would be invisible in a prompt-shaped response.
        const string Content = "Bí mật chỉ dành cho RAG đang bật.";

        // With RAG on, the very same fixture DOES inject the block — which is what makes the negative case below
        // meaningful rather than an artefact of a badly seeded database.
        await using (var on = await _database.CreateScenarioAsync(ragEnabled: true, embeddingsEnabled: true))
        {
            var onSeed = await SeedAsync(on);
            await InsertAndEmbedAsync(on, onSeed, "Quy ước", Content);

            var withRag = await PrepareAsync(on, onSeed, "Quy ước là gì?");
            Assert.Contains(Content, withRag.SystemPrompt, StringComparison.Ordinal);
        }

        await using var off = await _database.CreateScenarioAsync(ragEnabled: false, embeddingsEnabled: true);
        var seed = await SeedAsync(off);

        await InsertAndEmbedAsync(off, seed, "Quy ước", Content);

        var withoutRag = await PrepareAsync(off, seed, "Quy ước là gì?");

        Assert.DoesNotContain(KnowledgeChunking.ContextHeader, withoutRag.SystemPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain(Content, withoutRag.SystemPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Prepare_WithEmbeddingsOff_OmitsTheBlockEvenWhenRagIsOn()
    {
        // Both switches are required: with the embedder off there is no query vector, and calling the provider
        // anyway would spend money on a feature that is switched off.
        await using var scenario = await _database.CreateScenarioAsync(ragEnabled: true, embeddingsEnabled: false);
        var seed = await SeedAsync(scenario);

        const string Content = "Nội dung chỉ hiển thị khi embeddings bật.";

        await InsertDocumentWithVectorsAsync(scenario, seed, "Quy ước", Content);

        var prepared = await PrepareAsync(scenario, seed, "Quy ước là gì?");

        Assert.DoesNotContain(KnowledgeChunking.ContextHeader, prepared.SystemPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain(Content, prepared.SystemPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Prepare_NeverInjectsAnotherWorkspacesKnowledge()
    {
        // ⛔ The leak this catches: one workspace's documents answering another workspace's question. Scoping
        // comes from the board's workspace, never from a request body.
        await using var scenario = await _database.CreateScenarioAsync(ragEnabled: true, embeddingsEnabled: true);
        var seed = await SeedAsync(scenario);

        const string ForeignContent = "Tài liệu nội bộ của workspace khác, không được rò rỉ.";

        var outsider = await scenario.CreateUserAsync("Người ngoài");
        var foreignWorkspace = await scenario.CreateWorkspaceAsync(outsider, "Workspace Bí Mật");
        var (foreignBoard, foreignTodo, foreignDone) = await scenario.CreateBoardAsync(foreignWorkspace, "Board Bí Mật");

        var foreign = new Seed(outsider, foreignWorkspace, foreignBoard, foreignTodo, foreignDone);

        await InsertAndEmbedAsync(scenario, foreign, "Tài liệu mật", ForeignContent);

        // The same question, asked on OUR board.
        var prepared = await PrepareAsync(scenario, seed, "Tài liệu nội bộ của workspace khác là gì?");

        Assert.DoesNotContain(ForeignContent, prepared.SystemPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain(KnowledgeChunking.ContextHeader, prepared.SystemPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Prepare_WithADeletedDocument_DoesNotRetrieveIt()
    {
        // A soft-deleted document keeps its vectors (a re-approve is cheap) but must stop being retrievable
        // immediately — otherwise "delete" would only hide a row in a list.
        await using var scenario = await _database.CreateScenarioAsync(ragEnabled: true, embeddingsEnabled: true);
        var seed = await SeedAsync(scenario);

        const string Content = "Nội dung đã bị xoá khỏi bộ nhớ.";

        var documentId = await InsertAndEmbedAsync(scenario, seed, "Tài liệu đã xoá", Content);

        await using (var db = scenario.NewDbContext())
        {
            var document = await db.WorkspaceKnowledgeDocuments.SingleAsync(d => d.Id == documentId);
            document.Status = KnowledgeDocumentStatus.Deleted;
            await db.SaveChangesAsync();
        }

        var prepared = await PrepareAsync(scenario, seed, "Nội dung đã bị xoá là gì?");

        Assert.DoesNotContain(Content, prepared.SystemPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain(KnowledgeChunking.ContextHeader, prepared.SystemPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Prepare_KeepsThePhase145PromptContractIntactWithTheKnowledgeBlockPresent()
    {
        // Adding a section must not move the marker the offline provider branches on, nor loosen any rule: the
        // knowledge block is reference material, not an exemption from "do not invent data".
        await using var scenario = await _database.CreateScenarioAsync(ragEnabled: true, embeddingsEnabled: true);
        var seed = await SeedAsync(scenario);

        await InsertAndEmbedAsync(scenario, seed, "Quy ước", "Nội dung kiến thức dự án.");

        var prepared = await PrepareAsync(scenario, seed, "Quy ước là gì?");

        Assert.Contains(AgentChatPrompts.Marker, prepared.SystemPrompt, StringComparison.Ordinal);
        Assert.Contains("KHÔNG hiển thị raw ID, GUID hoặc UUID", prepared.SystemPrompt, StringComparison.Ordinal);
        Assert.Contains("Bạn CHƯA thực hiện bất kỳ thay đổi nào", prepared.SystemPrompt, StringComparison.Ordinal);

        // The block is bounded, so it cannot push the prompt past the configured ceiling.
        Assert.Contains("NGỮ CẢNH BẢNG:", prepared.SystemPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Prepare_WithAPersonaAndKnowledge_CarriesBothWithoutLosingEither()
    {
        // The persona block and the knowledge block are added by two different features; a regression in either
        // positioning rule would drop one of them silently.
        await using var scenario = await _database.CreateScenarioAsync(ragEnabled: true, embeddingsEnabled: true);
        var seed = await SeedAsync(scenario);

        const string Content = "Kiến thức dự án cần xuất hiện cùng persona.";

        await InsertAndEmbedAsync(scenario, seed, "Quy ước", Content);

        var prepared = await PrepareAsync(scenario, seed, "Quy ước là gì?", persona: PersonaCatalog.TechLeadKey);

        Assert.StartsWith(AgentChatPrompts.PersonaHeading, prepared.SystemPrompt, StringComparison.Ordinal);
        Assert.Contains("AI Tech Lead", prepared.SystemPrompt, StringComparison.Ordinal);
        Assert.Contains(Content, prepared.SystemPrompt, StringComparison.Ordinal);
    }

    // ---- helpers ------------------------------------------------------------

    private sealed record Seed(
        ApplicationUser User,
        Workspace Workspace,
        Board Board,
        BoardColumn Todo,
        BoardColumn Done);

    private static async Task<Seed> SeedAsync(TestScenario scenario)
    {
        var user = await scenario.CreateUserAsync("Chủ workspace");
        var workspace = await scenario.CreateWorkspaceAsync(user, "Workspace RAG");
        var (board, todo, done) = await scenario.CreateBoardAsync(workspace, "Board RAG");

        return new Seed(user, workspace, board, todo, done);
    }

    /// <summary>
    /// Inserts a document in <c>pending</c>, runs the embedding pass so its chunks become retrievable, and
    /// returns the document id.
    /// </summary>
    private static async Task<Guid> InsertAndEmbedAsync(
        TestScenario scenario, Seed seed, string title, string content)
    {
        var documentId = await InsertDocumentAsync(scenario, seed, title, content);

        await using var scope = scenario.Services.CreateAsyncScope();
        var runner = scope.ServiceProvider.GetRequiredService<IKnowledgeEmbeddingRunner>();

        var result = await runner.RunOnceAsync();

        Assert.Equal(1, result.DocumentsPromoted);

        await using var db = scenario.NewDbContext();
        var document = await db.WorkspaceKnowledgeDocuments.SingleAsync(d => d.Id == documentId);

        Assert.Equal(KnowledgeDocumentStatus.Ready, document.Status);

        return documentId;
    }

    /// <summary>
    /// Same, but embeds the vectors directly — used by the case where <c>Embeddings:Enabled</c> is false, so the
    /// runner cannot be used to prepare the fixture.
    /// </summary>
    private static async Task InsertDocumentWithVectorsAsync(
        TestScenario scenario, Seed seed, string title, string content)
    {
        await using var scope = scenario.Services.CreateAsyncScope();
        var provider = scope.ServiceProvider.GetRequiredService<IEmbeddingProvider>();

        var vector = (await provider.EmbedAsync([content]))[0];

        await using var db = scenario.NewDbContext();

        var document = new WorkspaceKnowledgeDocument
        {
            WorkspaceId = seed.Workspace.Id,
            Title = title,
            Content = content,
            Status = KnowledgeDocumentStatus.Ready,
            ChunkCount = 1,
            CreatedByUserId = seed.User.Id,
        };

        db.WorkspaceKnowledgeDocuments.Add(document);

        db.WorkspaceKnowledgeChunks.Add(new WorkspaceKnowledgeChunk
        {
            DocumentId = document.Id,
            WorkspaceId = seed.Workspace.Id,
            ChunkIndex = 0,
            Content = content,
            Embedding = new Vector(vector.ToArray()),
        });

        await db.SaveChangesAsync();
    }

    private static async Task<Guid> InsertDocumentAsync(
        TestScenario scenario, Seed seed, string title, string content)
    {
        await using var db = scenario.NewDbContext();

        var document = new WorkspaceKnowledgeDocument
        {
            WorkspaceId = seed.Workspace.Id,
            Title = title,
            Content = content,
            // Pending: the runner is what promotes it, so the fixture exercises the same path production uses.
            Status = KnowledgeDocumentStatus.Pending,
            CreatedByUserId = seed.User.Id,
        };

        db.WorkspaceKnowledgeDocuments.Add(document);

        db.WorkspaceKnowledgeChunks.Add(new WorkspaceKnowledgeChunk
        {
            DocumentId = document.Id,
            WorkspaceId = seed.Workspace.Id,
            ChunkIndex = 0,
            Content = content,
        });

        document.ChunkCount = 1;

        await db.SaveChangesAsync();
        return document.Id;
    }

    private static async Task<AgentChatPreparedRequest> PrepareAsync(
        TestScenario scenario, Seed seed, string question, string? persona = null)
    {
        await using var scope = scenario.Services.CreateAsyncScope();
        var chat = scope.ServiceProvider.GetRequiredService<IAgentChatService>();

        var request = new AgentChatSendRequest([new AiChatMessageRequest("user", question)], []);

        return await chat.PrepareAsync(seed.Board.Id, request, persona, seed.User.Id);
    }
}
