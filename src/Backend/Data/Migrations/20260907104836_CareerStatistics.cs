using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MphRead.Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class CareerStatistics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "AltFormKills",
                table: "career_aggregates",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "BipedKills",
                table: "career_aggregates",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "CurrentWinStreak",
                table: "career_aggregates",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "HeadshotKills",
                table: "career_aggregates",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "KillsAsPrime",
                table: "career_aggregates",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "LongestKillStreak",
                table: "career_aggregates",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "LongestWinStreak",
                table: "career_aggregates",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "NodesCaptured",
                table: "career_aggregates",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "OctolithScores",
                table: "career_aggregates",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "OutcomeSamples",
                table: "career_aggregates",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateIndex(
                name: "IX_career_aggregates_TrustClass_Dimension_Kills_PlayerId",
                table: "career_aggregates",
                columns: new[] { "TrustClass", "Dimension", "Kills", "PlayerId" });

            migrationBuilder.CreateIndex(
                name: "IX_career_aggregates_TrustClass_Dimension_Wins_PlayerId",
                table: "career_aggregates",
                columns: new[] { "TrustClass", "Dimension", "Wins", "PlayerId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_career_aggregates_TrustClass_Dimension_Kills_PlayerId",
                table: "career_aggregates");

            migrationBuilder.DropIndex(
                name: "IX_career_aggregates_TrustClass_Dimension_Wins_PlayerId",
                table: "career_aggregates");

            migrationBuilder.DropColumn(
                name: "AltFormKills",
                table: "career_aggregates");

            migrationBuilder.DropColumn(
                name: "BipedKills",
                table: "career_aggregates");

            migrationBuilder.DropColumn(
                name: "CurrentWinStreak",
                table: "career_aggregates");

            migrationBuilder.DropColumn(
                name: "HeadshotKills",
                table: "career_aggregates");

            migrationBuilder.DropColumn(
                name: "KillsAsPrime",
                table: "career_aggregates");

            migrationBuilder.DropColumn(
                name: "LongestKillStreak",
                table: "career_aggregates");

            migrationBuilder.DropColumn(
                name: "LongestWinStreak",
                table: "career_aggregates");

            migrationBuilder.DropColumn(
                name: "NodesCaptured",
                table: "career_aggregates");

            migrationBuilder.DropColumn(
                name: "OctolithScores",
                table: "career_aggregates");

            migrationBuilder.DropColumn(
                name: "OutcomeSamples",
                table: "career_aggregates");
        }
    }
}
