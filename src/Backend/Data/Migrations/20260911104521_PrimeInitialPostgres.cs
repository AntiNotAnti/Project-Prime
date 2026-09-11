using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MphRead.Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class PrimeInitialPostgres : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "prime");

            migrationBuilder.CreateTable(
                name: "accepted_matches",
                schema: "prime",
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
                    RatingStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RatingPolicyVersion = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    RatingIneligibilityReason = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accepted_matches", x => x.MatchId);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoles",
                schema: "prime",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "career_projection_state",
                schema: "prime",
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
                name: "players",
                schema: "prime",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: true),
                    SecurityStamp = table.Column<string>(type: "text", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true),
                    PhoneNumber = table.Column<string>(type: "text", nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_players", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoleClaims",
                schema: "prime",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetRoleClaims_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalSchema: "prime",
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserClaims",
                schema: "prime",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUserClaims_players_UserId",
                        column: x => x.UserId,
                        principalSchema: "prime",
                        principalTable: "players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserLogins",
                schema: "prime",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "text", nullable: false),
                    ProviderKey = table.Column<string>(type: "text", nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "text", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_AspNetUserLogins_players_UserId",
                        column: x => x.UserId,
                        principalSchema: "prime",
                        principalTable: "players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserRoles",
                schema: "prime",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalSchema: "prime",
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_players_UserId",
                        column: x => x.UserId,
                        principalSchema: "prime",
                        principalTable: "players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserTokens",
                schema: "prime",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    LoginProvider = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_AspNetUserTokens_players_UserId",
                        column: x => x.UserId,
                        principalSchema: "prime",
                        principalTable: "players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "hunter_licenses",
                schema: "prime",
                columns: table => new
                {
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RatingPoints = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_hunter_licenses", x => x.PlayerId);
                    table.ForeignKey(
                        name: "FK_hunter_licenses_players_PlayerId",
                        column: x => x.PlayerId,
                        principalSchema: "prime",
                        principalTable: "players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "player_profiles",
                schema: "prime",
                columns: table => new
                {
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    FavoriteHunter = table.Column<byte>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_player_profiles", x => x.PlayerId);
                    table.ForeignKey(
                        name: "FK_player_profiles_players_PlayerId",
                        column: x => x.PlayerId,
                        principalSchema: "prime",
                        principalTable: "players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "career_aggregates",
                schema: "prime",
                columns: table => new
                {
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    TrustClass = table.Column<int>(type: "integer", nullable: false),
                    Dimension = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Matches = table.Column<long>(type: "bigint", nullable: false),
                    Wins = table.Column<long>(type: "bigint", nullable: false),
                    Ties = table.Column<long>(type: "bigint", nullable: false),
                    Losses = table.Column<long>(type: "bigint", nullable: false),
                    PlayedTicks = table.Column<long>(type: "bigint", nullable: false),
                    Kills = table.Column<long>(type: "bigint", nullable: false),
                    Deaths = table.Column<long>(type: "bigint", nullable: false),
                    Assists = table.Column<long>(type: "bigint", nullable: false),
                    Damage = table.Column<long>(type: "bigint", nullable: false),
                    OctolithScores = table.Column<long>(type: "bigint", nullable: false),
                    NodesCaptured = table.Column<long>(type: "bigint", nullable: false),
                    KillsAsPrime = table.Column<long>(type: "bigint", nullable: false),
                    HeadshotKills = table.Column<long>(type: "bigint", nullable: false),
                    BipedKills = table.Column<long>(type: "bigint", nullable: true),
                    AltFormKills = table.Column<long>(type: "bigint", nullable: true),
                    LongestKillStreak = table.Column<long>(type: "bigint", nullable: false),
                    CurrentWinStreak = table.Column<long>(type: "bigint", nullable: false),
                    LongestWinStreak = table.Column<long>(type: "bigint", nullable: false),
                    OutcomeSamples = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_career_aggregates", x => new { x.PlayerId, x.TrustClass, x.Dimension, x.Key });
                    table.ForeignKey(
                        name: "FK_career_aggregates_hunter_licenses_PlayerId",
                        column: x => x.PlayerId,
                        principalSchema: "prime",
                        principalTable: "hunter_licenses",
                        principalColumn: "PlayerId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "career_participations",
                schema: "prime",
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
                        principalSchema: "prime",
                        principalTable: "accepted_matches",
                        principalColumn: "MatchId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_career_participations_hunter_licenses_PlayerId",
                        column: x => x.PlayerId,
                        principalSchema: "prime",
                        principalTable: "hunter_licenses",
                        principalColumn: "PlayerId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "rating_transactions",
                schema: "prime",
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
                        principalSchema: "prime",
                        principalTable: "accepted_matches",
                        principalColumn: "MatchId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_rating_transactions_hunter_licenses_PlayerId",
                        column: x => x.PlayerId,
                        principalSchema: "prime",
                        principalTable: "hunter_licenses",
                        principalColumn: "PlayerId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "rating_pair_contributions",
                schema: "prime",
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
                        principalSchema: "prime",
                        principalTable: "rating_transactions",
                        principalColumns: new[] { "MatchId", "PlayerId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                schema: "prime",
                table: "career_projection_state",
                columns: new[] { "Id", "RebuildRequired" },
                values: new object[] { 1, true });

            migrationBuilder.CreateIndex(
                name: "IX_accepted_matches_ProcessingOrder",
                schema: "prime",
                table: "accepted_matches",
                column: "ProcessingOrder",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetRoleClaims_RoleId",
                schema: "prime",
                table: "AspNetRoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                schema: "prime",
                table: "AspNetRoles",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserClaims_UserId",
                schema: "prime",
                table: "AspNetUserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserLogins_UserId",
                schema: "prime",
                table: "AspNetUserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserRoles_RoleId",
                schema: "prime",
                table: "AspNetUserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_career_aggregates_TrustClass_Dimension_Kills_PlayerId",
                schema: "prime",
                table: "career_aggregates",
                columns: new[] { "TrustClass", "Dimension", "Kills", "PlayerId" });

            migrationBuilder.CreateIndex(
                name: "IX_career_aggregates_TrustClass_Dimension_Wins_PlayerId",
                schema: "prime",
                table: "career_aggregates",
                columns: new[] { "TrustClass", "Dimension", "Wins", "PlayerId" });

            migrationBuilder.CreateIndex(
                name: "IX_career_participations_PlayerId_ProcessingOrder",
                schema: "prime",
                table: "career_participations",
                columns: new[] { "PlayerId", "ProcessingOrder" });

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                schema: "prime",
                table: "players",
                column: "NormalizedEmail",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                schema: "prime",
                table: "players",
                column: "NormalizedUserName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_rating_transactions_PlayerId_ProcessingOrder",
                schema: "prime",
                table: "rating_transactions",
                columns: new[] { "PlayerId", "ProcessingOrder" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AspNetRoleClaims",
                schema: "prime");

            migrationBuilder.DropTable(
                name: "AspNetUserClaims",
                schema: "prime");

            migrationBuilder.DropTable(
                name: "AspNetUserLogins",
                schema: "prime");

            migrationBuilder.DropTable(
                name: "AspNetUserRoles",
                schema: "prime");

            migrationBuilder.DropTable(
                name: "AspNetUserTokens",
                schema: "prime");

            migrationBuilder.DropTable(
                name: "career_aggregates",
                schema: "prime");

            migrationBuilder.DropTable(
                name: "career_participations",
                schema: "prime");

            migrationBuilder.DropTable(
                name: "career_projection_state",
                schema: "prime");

            migrationBuilder.DropTable(
                name: "player_profiles",
                schema: "prime");

            migrationBuilder.DropTable(
                name: "rating_pair_contributions",
                schema: "prime");

            migrationBuilder.DropTable(
                name: "AspNetRoles",
                schema: "prime");

            migrationBuilder.DropTable(
                name: "rating_transactions",
                schema: "prime");

            migrationBuilder.DropTable(
                name: "accepted_matches",
                schema: "prime");

            migrationBuilder.DropTable(
                name: "hunter_licenses",
                schema: "prime");

            migrationBuilder.DropTable(
                name: "players",
                schema: "prime");
        }
    }
}
