using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TeamNexus.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase147AgentPlatform : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "capacity_weight",
                table: "workspace_members",
                type: "double precision",
                nullable: false,
                defaultValue: 1.0);

            migrationBuilder.AddColumn<int>(
                name: "max_wip_limit",
                table: "workspace_members",
                type: "integer",
                nullable: false,
                defaultValue: 5);

            migrationBuilder.AddColumn<string>(
                name: "primary_role",
                table: "workspace_members",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "Member");

            migrationBuilder.AddColumn<string>(
                name: "skills",
                table: "workspace_members",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb");

            migrationBuilder.CreateTable(
                name: "workspace_taxonomies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    key = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    label = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    color_hex = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    position = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_workspace_taxonomies", x => x.id);
                    table.CheckConstraint("ck_workspace_taxonomies_kind", "\"kind\" IN ('domain', 'work_type')");
                    table.ForeignKey(
                        name: "fk_workspace_taxonomies_workspaces_workspace_id",
                        column: x => x.workspace_id,
                        principalTable: "workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_workspace_members_capacity_weight",
                table: "workspace_members",
                sql: "\"capacity_weight\" > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_workspace_members_max_wip_limit",
                table: "workspace_members",
                sql: "\"max_wip_limit\" >= 0");

            migrationBuilder.CreateIndex(
                name: "ix_workspace_taxonomies_lookup",
                table: "workspace_taxonomies",
                columns: new[] { "workspace_id", "kind", "position" });

            migrationBuilder.CreateIndex(
                name: "uq_workspace_taxonomies_key",
                table: "workspace_taxonomies",
                columns: new[] { "workspace_id", "kind", "key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "workspace_taxonomies");

            migrationBuilder.DropCheckConstraint(
                name: "ck_workspace_members_capacity_weight",
                table: "workspace_members");

            migrationBuilder.DropCheckConstraint(
                name: "ck_workspace_members_max_wip_limit",
                table: "workspace_members");

            migrationBuilder.DropColumn(
                name: "capacity_weight",
                table: "workspace_members");

            migrationBuilder.DropColumn(
                name: "max_wip_limit",
                table: "workspace_members");

            migrationBuilder.DropColumn(
                name: "primary_role",
                table: "workspace_members");

            migrationBuilder.DropColumn(
                name: "skills",
                table: "workspace_members");
        }
    }
}
