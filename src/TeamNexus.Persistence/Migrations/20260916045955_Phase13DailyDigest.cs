using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TeamNexus.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase13DailyDigest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "digest_enabled",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "digest_enabled",
                table: "users");
        }
    }
}
