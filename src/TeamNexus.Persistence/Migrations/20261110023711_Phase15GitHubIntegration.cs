using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TeamNexus.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase15GitHubIntegration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "github_issue_number",
                table: "tasks",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "github_issue_url",
                table: "tasks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "github_pr_number",
                table: "tasks",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "github_pr_status",
                table: "tasks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "github_pr_url",
                table: "tasks",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "webhook_delivery_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    delivery_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    event_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    error_message = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_webhook_delivery_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "workspace_integrations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "text", nullable: false),
                    repo_name = table.Column<string>(type: "text", nullable: true),
                    encrypted_pat = table.Column<string>(type: "text", nullable: true),
                    encrypted_webhook_secret = table.Column<string>(type: "text", nullable: true),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_workspace_integrations", x => x.id);
                    table.ForeignKey(
                        name: "fk_workspace_integrations_workspaces_workspace_id",
                        column: x => x.workspace_id,
                        principalTable: "workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_webhook_delivery_logs_delivery_id",
                table: "webhook_delivery_logs",
                column: "delivery_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_workspace_integrations_workspace_id_provider",
                table: "workspace_integrations",
                columns: new[] { "workspace_id", "provider" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "webhook_delivery_logs");

            migrationBuilder.DropTable(
                name: "workspace_integrations");

            migrationBuilder.DropColumn(
                name: "github_issue_number",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "github_issue_url",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "github_pr_number",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "github_pr_status",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "github_pr_url",
                table: "tasks");
        }
    }
}
