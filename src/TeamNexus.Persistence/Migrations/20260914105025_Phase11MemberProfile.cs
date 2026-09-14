using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TeamNexus.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase11MemberProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "email_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    to_email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    body_preview = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    sent_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    provider_message_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_email_messages", x => x.id);
                    table.CheckConstraint("ck_email_messages_status", "\"status\" IN ('Queued', 'Sent', 'Failed')");
                    table.ForeignKey(
                        name: "fk_email_messages_users_sent_by_user_id",
                        column: x => x.sent_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_email_messages_workspaces_workspace_id",
                        column: x => x.workspace_id,
                        principalTable: "workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "workspace_invitations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    invited_email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    invited_role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    token_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    invited_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    accepted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_workspace_invitations", x => x.id);
                    table.CheckConstraint("ck_workspace_invitations_invited_role", "\"invited_role\" IN ('Admin', 'Manager', 'Member')");
                    table.CheckConstraint("ck_workspace_invitations_status", "\"status\" IN ('Pending', 'Accepted', 'Cancelled', 'Expired')");
                    table.ForeignKey(
                        name: "fk_workspace_invitations_users_accepted_by_user_id",
                        column: x => x.accepted_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_workspace_invitations_users_invited_by_user_id",
                        column: x => x.invited_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_workspace_invitations_workspaces_workspace_id",
                        column: x => x.workspace_id,
                        principalTable: "workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_email_messages_sent_by_user_id",
                table: "email_messages",
                column: "sent_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_email_messages_workspace_id_created_at",
                table: "email_messages",
                columns: new[] { "workspace_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_workspace_invitations_accepted_by_user_id",
                table: "workspace_invitations",
                column: "accepted_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_workspace_invitations_invited_by_user_id",
                table: "workspace_invitations",
                column: "invited_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_workspace_invitations_workspace_id_status",
                table: "workspace_invitations",
                columns: new[] { "workspace_id", "status" });

            migrationBuilder.CreateIndex(
                name: "uq_workspace_invitations_pending",
                table: "workspace_invitations",
                columns: new[] { "workspace_id", "invited_email" },
                unique: true,
                filter: "\"status\" = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "uq_workspace_invitations_token_hash",
                table: "workspace_invitations",
                column: "token_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "email_messages");

            migrationBuilder.DropTable(
                name: "workspace_invitations");
        }
    }
}
