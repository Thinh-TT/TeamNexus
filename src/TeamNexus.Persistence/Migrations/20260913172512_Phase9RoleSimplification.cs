using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TeamNexus.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase9RoleSimplification : Migration
    {
        // Fixed Guids matching IdentityRoles.cs
        private const string AdminId  = "11111111-1111-1111-1111-111111111111";
        private const string ManagerId = "22222222-2222-2222-2222-222222222222";
        private const string UserId   = "33333333-3333-3333-3333-333333333333"; // was MemberId

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Rename the "Member" Identity role to "User".
            //    UserId guid stays the same (33333333-...) so user_roles rows are unaffected.
            migrationBuilder.Sql("""
                UPDATE roles
                SET    name            = 'User',
                       normalized_name = 'USER',
                       concurrency_stamp = '33333333-3333-3333-3333-333333333333'
                WHERE  id = '33333333-3333-3333-3333-333333333333';
                """);

            // 2. Reassign any user who had the "Manager" Identity role to "User" instead.
            migrationBuilder.Sql($"""
                UPDATE user_roles
                SET    role_id = '{UserId}'
                WHERE  role_id = '{ManagerId}'
                  AND  user_id NOT IN (
                       SELECT user_id FROM user_roles WHERE role_id = '{UserId}'
                  );
                """);

            // 3. Delete remaining user_roles rows pointing to Manager (already reassigned above
            //    or duplicates for users who already had both Manager + Member roles).
            migrationBuilder.Sql($"""
                DELETE FROM user_roles WHERE role_id = '{ManagerId}';
                """);

            // 4. Delete the "Manager" role row itself.
            migrationBuilder.Sql($"""
                DELETE FROM roles WHERE id = '{ManagerId}';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Re-insert the "Manager" role.
            migrationBuilder.Sql($"""
                INSERT INTO roles (id, name, normalized_name, concurrency_stamp)
                VALUES ('{ManagerId}', 'Manager', 'MANAGER', '{ManagerId}')
                ON CONFLICT (id) DO NOTHING;
                """);

            // Rename "User" back to "Member".
            migrationBuilder.Sql("""
                UPDATE roles
                SET    name            = 'Member',
                       normalized_name = 'MEMBER',
                       concurrency_stamp = '33333333-3333-3333-3333-333333333333'
                WHERE  id = '33333333-3333-3333-3333-333333333333';
                """);
        }
    }
}
