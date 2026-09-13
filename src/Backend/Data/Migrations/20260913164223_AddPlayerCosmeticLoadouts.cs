using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MphRead.Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPlayerCosmeticLoadouts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "player_cosmetic_loadouts",
                schema: "prime",
                columns: table => new
                {
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    hunter = table.Column<short>(type: "smallint", nullable: false),
                    skin_key = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false),
                    armor_effect_key = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false),
                    death_effect_key = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_player_cosmetic_loadouts", x => new { x.player_id, x.hunter });
                    table.ForeignKey(
                        name: "FK_player_cosmetic_loadouts_players_player_id",
                        column: x => x.player_id,
                        principalSchema: "prime",
                        principalTable: "players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "player_cosmetic_loadouts",
                schema: "prime");
        }
    }
}
