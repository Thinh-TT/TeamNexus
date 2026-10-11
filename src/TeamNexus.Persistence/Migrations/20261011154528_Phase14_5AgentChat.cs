using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TeamNexus.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase14_5AgentChat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "agent_chat_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    board_id = table.Column<Guid>(type: "uuid", nullable: true),
                    task_id = table.Column<Guid>(type: "uuid", nullable: true),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    role = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    attachments = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agent_chat_messages", x => x.id);
                    table.CheckConstraint("ck_agent_chat_messages_role", "\"role\" IN ('User', 'Assistant')");
                    table.CheckConstraint("ck_agent_chat_messages_scope", "num_nonnulls(\"board_id\", \"task_id\") = 1");
                    table.ForeignKey(
                        name: "fk_agent_chat_messages_asp_net_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_agent_chat_messages_boards_board_id",
                        column: x => x.board_id,
                        principalTable: "boards",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_agent_chat_messages_tasks_task_id",
                        column: x => x.task_id,
                        principalTable: "tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_agent_chat_messages_workspaces_workspace_id",
                        column: x => x.workspace_id,
                        principalTable: "workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_agent_chat_messages_board_id_created_at",
                table: "agent_chat_messages",
                columns: new[] { "board_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_agent_chat_messages_task_id",
                table: "agent_chat_messages",
                column: "task_id");

            migrationBuilder.CreateIndex(
                name: "ix_agent_chat_messages_user_id",
                table: "agent_chat_messages",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_agent_chat_messages_workspace_id_created_at",
                table: "agent_chat_messages",
                columns: new[] { "workspace_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agent_chat_messages");
        }
    }
}
