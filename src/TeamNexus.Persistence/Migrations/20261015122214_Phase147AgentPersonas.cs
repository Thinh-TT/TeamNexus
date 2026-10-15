using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TeamNexus.Persistence.Migrations
{
    /// <summary>
    /// Giai đoạn 14.7 Track B §B1 — the schema behind the team of specialised AI Agents.
    /// <para>
    /// <b>Hand-edited, and it must stay that way.</b> Two things EF Core cannot express are done with raw
    /// SQL and are the whole point of this migration:
    /// </para>
    /// <list type="number">
    ///   <item>
    ///     The <b>backfill</b> of <c>persona_key = 'general'</c>. Every agent row created before this
    ///     migration has no persona, and the Phase 7 lookup (<c>WHERE member_type = 'ai_agent'</c>) must keep
    ///     resolving to the same row afterwards. The backfill runs <b>before</b> the new index is created,
    ///     so the index can never be built over rows that would violate it.
    ///   </item>
    ///   <item>
    ///     The unique index on <c>(workspace_id, COALESCE(persona_key, 'general'))</c>. A plain unique index
    ///     on <c>(workspace_id, persona_key)</c> would be useless for the "one row per persona" rule:
    ///     PostgreSQL treats every <c>NULL</c> as distinct, so any number of agent rows with
    ///     <c>persona_key IS NULL</c> would be accepted. EF Core has no expression-index API, so the index
    ///     is written as SQL (the model still declares it by name so the snapshot stays reviewable).
    ///   </item>
    /// </list>
    /// <para>
    /// The order of operations is deliberate: <b>ADD COLUMN → BACKFILL → CREATE new index → DROP old index
    /// → ADD checks</b>. Creating the replacement before dropping the original means there is never a window
    /// in which neither constraint exists.
    /// </para>
    /// </summary>
    public partial class Phase147AgentPersonas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. The column itself. Nullable: humans must NOT carry a persona, and the CHECK added at the
            //    end of this method is what enforces that rather than a NOT NULL default.
            migrationBuilder.AddColumn<string>(
                name: "persona_key",
                table: "workspace_members",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            // 2. Backfill BEFORE the unique index exists. 'general' is the pre-existing agent's persona —
            //    it is literally the same virtual co-worker Phase 7 created, so no behaviour moves.
            migrationBuilder.Sql(
                "UPDATE workspace_members SET persona_key = 'general' "
                + "WHERE member_type = 'ai_agent' AND persona_key IS NULL;");

            // 3. The replacement constraint, built on COALESCE so a NULL persona counts as 'general' and the
            //    "one row per persona" rule is actually enforceable (see the class remarks).
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX uq_workspace_members_ai_persona "
                + "ON workspace_members (workspace_id, COALESCE(persona_key, 'general')) "
                + "WHERE member_type = 'ai_agent';");

            // 4. The old "exactly one agent per workspace" index goes last, after the new one exists.
            migrationBuilder.DropIndex(
                name: "uq_workspace_members_ai_agent",
                table: "workspace_members");

            // 5. The closed persona vocabulary, and the rule that only agent rows may carry one. Two separate
            //    constraints because they fail for different reasons and an operator should be able to tell
            //    "typo in the persona key" apart from "somebody gave a human a persona".
            migrationBuilder.AddCheckConstraint(
                name: "ck_workspace_members_persona_key",
                table: "workspace_members",
                sql: "\"persona_key\" IS NULL OR \"persona_key\" IN "
                    + "('general', 'scrum_master', 'tech_lead', 'qa_security')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_workspace_members_persona_human",
                table: "workspace_members",
                sql: "\"member_type\" <> 'human' OR \"persona_key\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverse order. The human/persona CHECK is dropped first because it is the only thing that
            // could block the column drop below; the old unique index is recreated BEFORE the column goes,
            // for the same "never an unconstrained window" reason as in Up.
            migrationBuilder.DropCheckConstraint(
                name: "ck_workspace_members_persona_human",
                table: "workspace_members");

            migrationBuilder.DropCheckConstraint(
                name: "ck_workspace_members_persona_key",
                table: "workspace_members");

            migrationBuilder.Sql("DROP INDEX IF EXISTS uq_workspace_members_ai_persona;");

            migrationBuilder.CreateIndex(
                name: "uq_workspace_members_ai_agent",
                table: "workspace_members",
                column: "workspace_id",
                unique: true,
                filter: "\"member_type\" = 'ai_agent'");

            migrationBuilder.DropColumn(
                name: "persona_key",
                table: "workspace_members");
        }
    }
}
