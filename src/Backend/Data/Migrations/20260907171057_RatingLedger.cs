using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MphRead.Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class RatingLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RatingPoints",
                table: "hunter_licenses",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "Losses",
                table: "career_aggregates",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<int>(
                name: "RatingIneligibilityReason",
                table: "accepted_matches",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RatingPolicyVersion",
                table: "accepted_matches",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            // Reports accepted before this migration used schema 1. Preserve their career facts
            // while replacing policyPending and legacy ParticipantOutcome integers explicitly.
            migrationBuilder.Sql("""
                UPDATE accepted_matches
                SET "RatingStatus" = 'ineligible',
                    "RatingPolicyVersion" = 1,
                    "RatingIneligibilityReason" = 1
                WHERE "RatingStatus" = 'policyPending';
                """);
            migrationBuilder.Sql("""
                UPDATE career_participations
                SET "Outcome" = CASE
                        WHEN NOT "Eligible" THEN 5
                        WHEN "Outcome" = 0 AND "Won" THEN 0
                        WHEN "Outcome" = 0 AND "Tied" THEN 2
                        WHEN "Outcome" = 0 THEN 1
                        WHEN "Outcome" = 2 THEN 3
                        ELSE 5
                    END,
                    "Eligible" = "Eligible" AND "Outcome" IN (0, 2);
                """);
            migrationBuilder.Sql("""
                UPDATE career_aggregates
                SET "Losses" = GREATEST(0,
                    CASE WHEN "Dimension" = 'hunter' THEN "OutcomeSamples" ELSE "Matches" END
                    - "Wins" - "Ties")
                WHERE "Dimension" <> 'weapon';
                """);
            // Legacy weapon Matches counted every weapon on every match. There is no truthful
            // MatchesUsed backfill without replaying raw reports, so omit these projections until
            // the mandatory operator rebuild restores them from authoritative BeamKills.
            migrationBuilder.Sql("""
                DELETE FROM career_aggregates WHERE "Dimension" = 'weapon';
                """);

            migrationBuilder.CreateTable(
                name: "career_projection_state",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RebuildRequired = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_career_projection_state", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "rating_transactions",
                columns: table => new
                {
                    MatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProcessingOrder = table.Column<long>(type: "bigint", nullable: false),
                    PointsBefore = table.Column<int>(type: "integer", nullable: false),
                    TierBefore = table.Column<int>(type: "integer", nullable: false),
                    OpponentCount = table.Column<int>(type: "integer", nullable: false),
                    RawDelta = table.Column<int>(type: "integer", nullable: false),
                    NormalizedDelta = table.Column<int>(type: "integer", nullable: false),
                    AppliedDelta = table.Column<int>(type: "integer", nullable: false),
                    PointsAfter = table.Column<int>(type: "integer", nullable: false),
                    TierAfter = table.Column<int>(type: "integer", nullable: false),
                    PolicyVersion = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rating_transactions", x => new { x.MatchId, x.PlayerId });
                    table.ForeignKey(
                        name: "FK_rating_transactions_accepted_matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "accepted_matches",
                        principalColumn: "MatchId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_rating_transactions_hunter_licenses_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "hunter_licenses",
                        principalColumn: "PlayerId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "rating_pair_contributions",
                columns: table => new
                {
                    MatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    OpponentPlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    OpponentPointsBefore = table.Column<int>(type: "integer", nullable: false),
                    OpponentTierBefore = table.Column<int>(type: "integer", nullable: false),
                    Result = table.Column<int>(type: "integer", nullable: false),
                    Delta = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rating_pair_contributions", x => new { x.MatchId, x.PlayerId, x.OpponentPlayerId });
                    table.ForeignKey(
                        name: "FK_rating_pair_contributions_rating_transactions_MatchId_Playe~",
                        columns: x => new { x.MatchId, x.PlayerId },
                        principalTable: "rating_transactions",
                        principalColumns: new[] { "MatchId", "PlayerId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "career_projection_state",
                columns: new[] { "Id", "RebuildRequired" },
                values: new object[] { 1, true });

            migrationBuilder.CreateIndex(
                name: "IX_rating_transactions_PlayerId_ProcessingOrder",
                table: "rating_transactions",
                columns: new[] { "PlayerId", "ProcessingOrder" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "career_projection_state");

            migrationBuilder.DropTable(
                name: "rating_pair_contributions");

            migrationBuilder.DropTable(
                name: "rating_transactions");

            migrationBuilder.DropColumn(
                name: "RatingPoints",
                table: "hunter_licenses");

            migrationBuilder.DropColumn(
                name: "Losses",
                table: "career_aggregates");

            migrationBuilder.DropColumn(
                name: "RatingIneligibilityReason",
                table: "accepted_matches");

            migrationBuilder.DropColumn(
                name: "RatingPolicyVersion",
                table: "accepted_matches");
        }
    }
}
