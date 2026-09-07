using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MphRead.Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class MatchLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "accepted_matches",
                columns: table => new
                {
                    MatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProcessingOrder = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ServerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ServerIncarnation = table.Column<Guid>(type: "uuid", nullable: false),
                    PayloadHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OriginalReport = table.Column<byte[]>(type: "bytea", nullable: false),
                    AcceptedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RoomKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Mode = table.Column<int>(type: "integer", nullable: false),
                    TrustClass = table.Column<int>(type: "integer", nullable: false),
                    CareerEligible = table.Column<bool>(type: "boolean", nullable: false),
                    RatingStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accepted_matches", x => x.MatchId);
                });

            migrationBuilder.CreateTable(
                name: "career_aggregates",
                columns: table => new
                {
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    TrustClass = table.Column<int>(type: "integer", nullable: false),
                    Dimension = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Matches = table.Column<long>(type: "bigint", nullable: false),
                    Wins = table.Column<long>(type: "bigint", nullable: false),
                    Ties = table.Column<long>(type: "bigint", nullable: false),
                    PlayedTicks = table.Column<long>(type: "bigint", nullable: false),
                    Kills = table.Column<long>(type: "bigint", nullable: false),
                    Deaths = table.Column<long>(type: "bigint", nullable: false),
                    Assists = table.Column<long>(type: "bigint", nullable: false),
                    Damage = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_career_aggregates", x => new { x.PlayerId, x.TrustClass, x.Dimension, x.Key });
                    table.ForeignKey(
                        name: "FK_career_aggregates_hunter_licenses_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "hunter_licenses",
                        principalColumn: "PlayerId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "career_participations",
                columns: table => new
                {
                    MatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProcessingOrder = table.Column<long>(type: "bigint", nullable: false),
                    Eligible = table.Column<bool>(type: "boolean", nullable: false),
                    Won = table.Column<bool>(type: "boolean", nullable: false),
                    Tied = table.Column<bool>(type: "boolean", nullable: false),
                    Outcome = table.Column<int>(type: "integer", nullable: false),
                    PlayedTicks = table.Column<long>(type: "bigint", nullable: false),
                    Kills = table.Column<long>(type: "bigint", nullable: false),
                    Deaths = table.Column<long>(type: "bigint", nullable: false),
                    Assists = table.Column<long>(type: "bigint", nullable: false),
                    Damage = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_career_participations", x => new { x.MatchId, x.PlayerId });
                    table.ForeignKey(
                        name: "FK_career_participations_accepted_matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "accepted_matches",
                        principalColumn: "MatchId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_career_participations_hunter_licenses_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "hunter_licenses",
                        principalColumn: "PlayerId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_accepted_matches_ProcessingOrder",
                table: "accepted_matches",
                column: "ProcessingOrder",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_career_participations_PlayerId_ProcessingOrder",
                table: "career_participations",
                columns: new[] { "PlayerId", "ProcessingOrder" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "career_aggregates");

            migrationBuilder.DropTable(
                name: "career_participations");

            migrationBuilder.DropTable(
                name: "accepted_matches");
        }
    }
}
