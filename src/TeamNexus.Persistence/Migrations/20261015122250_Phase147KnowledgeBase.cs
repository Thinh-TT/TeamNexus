using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace TeamNexus.Persistence.Migrations
{
    /// <summary>
    /// Giai đoạn 14.7 Track B §B3 — the Workspace RAG schema: the indexed documents, their embedded slices,
    /// and the HNSW index retrieval runs on.
    /// <para>
    /// <b>Hand-edited for one reason: the extension.</b> EF Core records
    /// <c>modelBuilder.HasPostgresExtension("vector")</c> as an <c>AlterDatabase()</c> annotation and does
    /// <b>not</b> emit <c>CREATE EXTENSION</c>, so the statement is written as raw SQL and deliberately
    /// placed <b>before</b> the first <c>vector(1536)</c> column: the tables cannot be created without it.
    /// </para>
    /// <para>
    /// <b>The index operator class is not decorative.</b> Retrieval ranks with the cosine distance operator
    /// (<c>&lt;=&gt;</c>, EF's <c>CosineDistance</c>), and PostgreSQL only uses an index whose operator class
    /// matches the operator in the query — hence <c>vector_cosine_ops</c> rather than the L2 default. An
    /// index built for <c>vector_l2_ops</c> would exist, look correct in <c>\d</c>, and never be used.
    /// </para>
    /// <para>
    /// <b>Requires the <c>pgvector</c> image.</b> <c>vector</c> is a third-party extension, not a contrib
    /// module, so <c>postgres:18</c> cannot run this migration (SQLSTATE 0A000 / 58P01). CI and local dev use
    /// <c>pgvector/pgvector:pg18</c> — see project-docs/02-tech-stack-decisions.md §3.
    /// </para>
    /// </summary>
    public partial class Phase147KnowledgeBase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // MUST come before any `vector(N)` column below. `IF NOT EXISTS` so re-running against a database
            // where an operator already enabled it by hand is not an error.
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS vector;");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:vector", ",,");

            migrationBuilder.CreateTable(
                name: "workspace_knowledge_documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    source_url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    content = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    chunk_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_workspace_knowledge_documents", x => x.id);
                    table.CheckConstraint("ck_workspace_knowledge_documents_status", "\"status\" IN ('pending', 'ready', 'failed', 'deleted')");
                    table.ForeignKey(
                        name: "fk_workspace_knowledge_documents_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_workspace_knowledge_documents_workspaces_workspace_id",
                        column: x => x.workspace_id,
                        principalTable: "workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "workspace_knowledge_chunks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    // Denormalised from the document so retrieval is a single-table cosine query.
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    chunk_index = table.Column<int>(type: "integer", nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    // Nullable, and NOT a flag: `embedding IS NULL` IS the "not embedded yet" predicate the
                    // embedding runner scans, so a separate pending flag column would be redundant state.
                    embedding = table.Column<Vector>(type: "vector(1536)", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_workspace_knowledge_chunks", x => x.id);
                    table.ForeignKey(
                        name: "fk_workspace_knowledge_chunks_workspace_knowledge_document_doc",
                        column: x => x.document_id,
                        principalTable: "workspace_knowledge_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            // HNSW + vector_cosine_ops: the index the retrieval query can actually use.
            migrationBuilder.CreateIndex(
                name: "ix_workspace_knowledge_chunks_embedding",
                table: "workspace_knowledge_chunks",
                column: "embedding")
                .Annotation("Npgsql:IndexMethod", "hnsw")
                .Annotation("Npgsql:IndexOperators", new[] { "vector_cosine_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_workspace_knowledge_chunks_workspace",
                table: "workspace_knowledge_chunks",
                column: "workspace_id");

            // One position per document: the idempotency guard if the same document is submitted twice.
            migrationBuilder.CreateIndex(
                name: "uq_workspace_knowledge_chunks_position",
                table: "workspace_knowledge_chunks",
                columns: new[] { "document_id", "chunk_index" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_workspace_knowledge_documents_created_by_user_id",
                table: "workspace_knowledge_documents",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_workspace_knowledge_documents_lookup",
                table: "workspace_knowledge_documents",
                columns: new[] { "workspace_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The extension itself is deliberately NOT dropped: it may predate this migration, and other
            // objects in the database could depend on it. Dropping it would be a destructive surprise for an
            // operation that is supposed to undo only what this migration added.
            migrationBuilder.DropTable(
                name: "workspace_knowledge_chunks");

            migrationBuilder.DropTable(
                name: "workspace_knowledge_documents");

            migrationBuilder.AlterDatabase()
                .OldAnnotation("Npgsql:PostgresExtension:vector", ",,");
        }
    }
}
