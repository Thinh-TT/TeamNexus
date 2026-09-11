using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TeamNexus.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase7AiAgentSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ai_agent_name",
                table: "workspace_members",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "member_type",
                table: "workspace_members",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "human");

            migrationBuilder.AddColumn<bool>(
                name: "is_clarification",
                table: "board_columns",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "agent_runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    board_id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    triggered_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    stop_reason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    clarification_question = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    clarification_comment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    resolution_comment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    previous_run_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tool_call_trace = table.Column<string>(type: "jsonb", nullable: false),
                    trace_truncated = table.Column<bool>(type: "boolean", nullable: false),
                    tool_call_count = table.Column<int>(type: "integer", nullable: false),
                    llm_call_count = table.Column<int>(type: "integer", nullable: false),
                    prompt_tokens = table.Column<int>(type: "integer", nullable: false),
                    completion_tokens = table.Column<int>(type: "integer", nullable: false),
                    output_kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    ai_action_log_id = table.Column<Guid>(type: "uuid", nullable: true),
                    notification_sent = table.Column<bool>(type: "boolean", nullable: false),
                    error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agent_runs", x => x.id);
                    table.CheckConstraint("ck_agent_runs_output_kind", "\"output_kind\" IS NULL OR \"output_kind\" IN ('Comment', 'Attachment')");
                    table.CheckConstraint("ck_agent_runs_status", "\"status\" IN ('Running', 'AwaitingClarification', 'AwaitingApproval', 'Completed', 'Failed')");
                    table.CheckConstraint("ck_agent_runs_stop_reason", "\"stop_reason\" IN ('DraftProduced', 'QuestionAsked', 'ToolLimit', 'TimeLimit', 'TokenBudget', 'ProviderError', 'Cancelled', 'TaskChanged', 'InternalError')");
                    table.ForeignKey(
                        name: "fk_agent_runs_agent_runs_previous_run_id",
                        column: x => x.previous_run_id,
                        principalTable: "agent_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_agent_runs_ai_action_logs_ai_action_log_id",
                        column: x => x.ai_action_log_id,
                        principalTable: "ai_action_logs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_agent_runs_asp_net_users_agent_user_id",
                        column: x => x.agent_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_agent_runs_asp_net_users_triggered_by_user_id",
                        column: x => x.triggered_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_agent_runs_boards_board_id",
                        column: x => x.board_id,
                        principalTable: "boards",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_agent_runs_task_comments_clarification_comment_id",
                        column: x => x.clarification_comment_id,
                        principalTable: "task_comments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_agent_runs_task_comments_resolution_comment_id",
                        column: x => x.resolution_comment_id,
                        principalTable: "task_comments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_agent_runs_tasks_task_id",
                        column: x => x.task_id,
                        principalTable: "tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_agent_runs_workspaces_workspace_id",
                        column: x => x.workspace_id,
                        principalTable: "workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "task_attachments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_run_id = table.Column<Guid>(type: "uuid", nullable: true),
                    file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    content_type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    size_bytes = table.Column<int>(type: "integer", nullable: false),
                    content = table.Column<byte[]>(type: "bytea", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_task_attachments", x => x.id);
                    table.ForeignKey(
                        name: "fk_task_attachments_agent_runs_source_run_id",
                        column: x => x.source_run_id,
                        principalTable: "agent_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_task_attachments_tasks_task_id",
                        column: x => x.task_id,
                        principalTable: "tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_task_attachments_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "uq_workspace_members_ai_agent",
                table: "workspace_members",
                column: "workspace_id",
                unique: true,
                filter: "\"member_type\" = 'ai_agent'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_workspace_members_member_type",
                table: "workspace_members",
                sql: "\"member_type\" IN ('human', 'ai_agent')");

            migrationBuilder.CreateIndex(
                name: "uq_board_columns_clarification",
                table: "board_columns",
                column: "board_id",
                unique: true,
                filter: "\"is_clarification\"");

            migrationBuilder.CreateIndex(
                name: "ix_agent_runs_agent_user_id",
                table: "agent_runs",
                column: "agent_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_agent_runs_ai_action_log_id",
                table: "agent_runs",
                column: "ai_action_log_id");

            migrationBuilder.CreateIndex(
                name: "ix_agent_runs_board_id",
                table: "agent_runs",
                column: "board_id");

            migrationBuilder.CreateIndex(
                name: "ix_agent_runs_clarification_comment_id",
                table: "agent_runs",
                column: "clarification_comment_id");

            migrationBuilder.CreateIndex(
                name: "ix_agent_runs_previous_run_id",
                table: "agent_runs",
                column: "previous_run_id");

            migrationBuilder.CreateIndex(
                name: "ix_agent_runs_resolution_comment_id",
                table: "agent_runs",
                column: "resolution_comment_id");

            migrationBuilder.CreateIndex(
                name: "ix_agent_runs_status",
                table: "agent_runs",
                column: "status",
                filter: "\"status\" = 'Running'");

            migrationBuilder.CreateIndex(
                name: "ix_agent_runs_task_id_started_at",
                table: "agent_runs",
                columns: new[] { "task_id", "started_at" });

            migrationBuilder.CreateIndex(
                name: "ix_agent_runs_triggered_by_user_id",
                table: "agent_runs",
                column: "triggered_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_agent_runs_workspace_id_started_at",
                table: "agent_runs",
                columns: new[] { "workspace_id", "started_at" });

            migrationBuilder.CreateIndex(
                name: "ix_task_attachments_created_by_user_id",
                table: "task_attachments",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_attachments_source_run_id",
                table: "task_attachments",
                column: "source_run_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_attachments_task_id",
                table: "task_attachments",
                column: "task_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "task_attachments");

            migrationBuilder.DropTable(
                name: "agent_runs");

            migrationBuilder.DropIndex(
                name: "uq_workspace_members_ai_agent",
                table: "workspace_members");

            migrationBuilder.DropCheckConstraint(
                name: "ck_workspace_members_member_type",
                table: "workspace_members");

            migrationBuilder.DropIndex(
                name: "uq_board_columns_clarification",
                table: "board_columns");

            migrationBuilder.DropColumn(
                name: "ai_agent_name",
                table: "workspace_members");

            migrationBuilder.DropColumn(
                name: "member_type",
                table: "workspace_members");

            migrationBuilder.DropColumn(
                name: "is_clarification",
                table: "board_columns");
        }
    }
}
