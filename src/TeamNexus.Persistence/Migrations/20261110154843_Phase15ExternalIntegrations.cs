using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TeamNexus.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase15ExternalIntegrations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "next_task_number",
                table: "workspaces",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.AddColumn<bool>(
                name: "is_system",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "task_number",
                table: "tasks",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<bool>(
                name: "is_in_review",
                table: "board_columns",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "task_external_links",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "github"),
                    link_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    external_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    external_url = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "open"),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_task_external_links", x => x.id);
                    table.CheckConstraint("ck_task_external_links_link_type", "\"link_type\" IN ('pull_request', 'issue')");
                    table.CheckConstraint("ck_task_external_links_provider", "\"provider\" IN ('github')");
                    table.CheckConstraint("ck_task_external_links_state", "\"state\" IN ('draft', 'open', 'merged', 'closed')");
                    table.ForeignKey(
                        name: "fk_task_external_links_tasks_task_id",
                        column: x => x.task_id,
                        principalTable: "tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_task_external_links_workspaces_workspace_id",
                        column: x => x.workspace_id,
                        principalTable: "workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "task_numbers",
                columns: table => new
                {
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_number = table.Column<long>(type: "bigint", nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_task_numbers", x => new { x.workspace_id, x.task_number });
                    table.ForeignKey(
                        name: "fk_task_numbers_tasks_task_id",
                        column: x => x.task_id,
                        principalTable: "tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_task_numbers_workspaces_workspace_id",
                        column: x => x.workspace_id,
                        principalTable: "workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "workspace_github_integrations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    webhook_token = table.Column<Guid>(type: "uuid", nullable: false),
                    secret_ciphertext = table.Column<string>(type: "text", nullable: false),
                    secret_hint = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    default_board_id = table.Column<Guid>(type: "uuid", nullable: true),
                    issue_label_filter = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    auto_move_on_pr = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    auto_create_tasks_from_issues = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    sync_issue_comments = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_workspace_github_integrations", x => x.id);
                    table.ForeignKey(
                        name: "fk_workspace_github_integrations_boards_default_board_id",
                        column: x => x.default_board_id,
                        principalTable: "boards",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_workspace_github_integrations_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_workspace_github_integrations_workspaces_workspace_id",
                        column: x => x.workspace_id,
                        principalTable: "workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "workspace_webhook_endpoints",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    target_url = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    subscribed_events = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    failure_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    last_success_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_failure_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_workspace_webhook_endpoints", x => x.id);
                    table.CheckConstraint("ck_workspace_webhook_endpoints_provider", "\"provider\" IN ('slack', 'discord')");
                    table.ForeignKey(
                        name: "fk_workspace_webhook_endpoints_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_workspace_webhook_endpoints_workspaces_workspace_id",
                        column: x => x.workspace_id,
                        principalTable: "workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "github_webhook_deliveries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    integration_id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    delivery_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    event_name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    repository_full_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "Processed"),
                    summary = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    error_message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_github_webhook_deliveries", x => x.id);
                    table.CheckConstraint("ck_github_webhook_deliveries_status", "\"status\" IN ('Processed', 'Ignored', 'Failed')");
                    table.ForeignKey(
                        name: "fk_github_webhook_deliveries_workspace_git_hub_integrations_in",
                        column: x => x.integration_id,
                        principalTable: "workspace_github_integrations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_github_webhook_deliveries_workspaces_workspace_id",
                        column: x => x.workspace_id,
                        principalTable: "workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "webhook_delivery_outbox",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    endpoint_id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    notification_id = table.Column<Guid>(type: "uuid", nullable: true),
                    event_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    dedupe_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    payload_json = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "Pending"),
                    attempt_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    max_attempts = table.Column<int>(type: "integer", nullable: false, defaultValue: 5),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_http_status = table.Column<int>(type: "integer", nullable: true),
                    last_error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_webhook_delivery_outbox", x => x.id);
                    table.CheckConstraint("ck_webhook_delivery_outbox_status", "\"status\" IN ('Pending', 'Sent', 'Failed')");
                    table.ForeignKey(
                        name: "fk_webhook_delivery_outbox_workspace_webhook_endpoints_endpoin",
                        column: x => x.endpoint_id,
                        principalTable: "workspace_webhook_endpoints",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_webhook_delivery_outbox_workspaces_workspace_id",
                        column: x => x.workspace_id,
                        principalTable: "workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_tasks_board_id_task_number",
                table: "tasks",
                columns: new[] { "board_id", "task_number" });

            migrationBuilder.CreateIndex(
                name: "IX_board_columns_board_id_single_in_review",
                table: "board_columns",
                column: "board_id",
                unique: true,
                filter: "is_in_review = true");

            migrationBuilder.CreateIndex(
                name: "ix_github_webhook_deliveries_integration_received",
                table: "github_webhook_deliveries",
                columns: new[] { "integration_id", "received_at" });

            migrationBuilder.CreateIndex(
                name: "ix_github_webhook_deliveries_workspace_id",
                table: "github_webhook_deliveries",
                column: "workspace_id");

            migrationBuilder.CreateIndex(
                name: "uq_github_webhook_deliveries_integration_delivery",
                table: "github_webhook_deliveries",
                columns: new[] { "integration_id", "delivery_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_task_external_links_workspace_lookup",
                table: "task_external_links",
                columns: new[] { "workspace_id", "provider", "link_type", "external_id" });

            migrationBuilder.CreateIndex(
                name: "uq_task_external_links_task_external",
                table: "task_external_links",
                columns: new[] { "task_id", "provider", "link_type", "external_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_task_numbers_task_id",
                table: "task_numbers",
                column: "task_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_webhook_delivery_outbox_endpoint_created",
                table: "webhook_delivery_outbox",
                columns: new[] { "endpoint_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_webhook_delivery_outbox_status_next_attempt",
                table: "webhook_delivery_outbox",
                columns: new[] { "status", "next_attempt_at" });

            migrationBuilder.CreateIndex(
                name: "ix_webhook_delivery_outbox_workspace_id",
                table: "webhook_delivery_outbox",
                column: "workspace_id");

            migrationBuilder.CreateIndex(
                name: "ix_workspace_github_integrations_created_by_user_id",
                table: "workspace_github_integrations",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_workspace_github_integrations_default_board_id",
                table: "workspace_github_integrations",
                column: "default_board_id");

            migrationBuilder.CreateIndex(
                name: "uq_workspace_github_integrations_webhook_token",
                table: "workspace_github_integrations",
                column: "webhook_token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_workspace_github_integrations_workspace_id",
                table: "workspace_github_integrations",
                column: "workspace_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_workspace_webhook_endpoints_created_by_user_id",
                table: "workspace_webhook_endpoints",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_workspace_webhook_endpoints_workspace_active",
                table: "workspace_webhook_endpoints",
                columns: new[] { "workspace_id", "is_active" });

            // Backfill sequential task_number (1..N) per workspace ordered by (created_at ASC, id ASC),
            // populate task_numbers, and advance workspaces.next_task_number = max + 1 (or 1 if empty).
            migrationBuilder.Sql(
                """
                WITH numbered AS (
                    SELECT t.id AS task_id,
                           b.workspace_id,
                           t.created_at,
                           ROW_NUMBER() OVER (PARTITION BY b.workspace_id ORDER BY t.created_at ASC, t.id ASC) AS seq_num
                    FROM tasks t
                    INNER JOIN boards b ON b.id = t.board_id
                )
                UPDATE tasks AS t
                SET task_number = n.seq_num
                FROM numbered AS n
                WHERE t.id = n.task_id;

                INSERT INTO task_numbers (workspace_id, task_number, task_id, created_at)
                SELECT b.workspace_id, t.task_number, t.id, t.created_at
                FROM tasks t
                INNER JOIN boards b ON b.id = t.board_id
                WHERE t.task_number > 0
                ON CONFLICT DO NOTHING;

                UPDATE workspaces AS w
                SET next_task_number = COALESCE(
                    (SELECT MAX(tn.task_number) + 1 FROM task_numbers tn WHERE tn.workspace_id = w.id),
                    1
                );
                """);

            // Seed deterministic system user "GitHub Bot" (00000000-0000-0000-0000-00000000b071).
            migrationBuilder.Sql(
                """
                INSERT INTO users (
                    id, user_name, normalized_user_name, email, normalized_email,
                    email_confirmed, security_stamp, concurrency_stamp,
                    phone_number_confirmed, two_factor_enabled, lockout_enabled, access_failed_count,
                    display_name, digest_enabled, is_system, created_at
                ) VALUES (
                    '00000000-0000-0000-0000-00000000b071',
                    'github-bot',
                    'GITHUB-BOT',
                    'github-bot@system.teamnexus.invalid',
                    'GITHUB-BOT@SYSTEM.TEAMNEXUS.INVALID',
                    true,
                    '00000000-0000-0000-0000-00000000b071',
                    '00000000-0000-0000-0000-00000000b071',
                    false, false, false, 0,
                    'GitHub Bot',
                    false,
                    true,
                    TIMESTAMPTZ '2026-01-01 00:00:00Z'
                )
                ON CONFLICT (id) DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DELETE FROM users WHERE id = '00000000-0000-0000-0000-00000000b071' AND is_system = true;");

            migrationBuilder.DropTable(
                name: "github_webhook_deliveries");

            migrationBuilder.DropTable(
                name: "task_external_links");

            migrationBuilder.DropTable(
                name: "task_numbers");

            migrationBuilder.DropTable(
                name: "webhook_delivery_outbox");

            migrationBuilder.DropTable(
                name: "workspace_github_integrations");

            migrationBuilder.DropTable(
                name: "workspace_webhook_endpoints");

            migrationBuilder.DropIndex(
                name: "ix_tasks_board_id_task_number",
                table: "tasks");

            migrationBuilder.DropIndex(
                name: "IX_board_columns_board_id_single_in_review",
                table: "board_columns");

            migrationBuilder.DropColumn(
                name: "next_task_number",
                table: "workspaces");

            migrationBuilder.DropColumn(
                name: "is_system",
                table: "users");

            migrationBuilder.DropColumn(
                name: "task_number",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "is_in_review",
                table: "board_columns");
        }
    }
}
