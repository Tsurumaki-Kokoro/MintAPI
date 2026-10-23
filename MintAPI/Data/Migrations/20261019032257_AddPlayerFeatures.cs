using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MintAPI.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPlayerFeatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "best_play_snapshot",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "int", nullable: false),
                    GameMode = table.Column<int>(type: "int", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    ObservedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    Payload = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_best_play_snapshot", x => new { x.UserId, x.GameMode, x.Date });
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "player_subscription",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    GameMode = table.Column<int>(type: "int", nullable: false),
                    Scope = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    StartCursor = table.Column<long>(type: "bigint", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_player_subscription", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "player_watch",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "int", nullable: false),
                    GameMode = table.Column<int>(type: "int", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    FloorCursor = table.Column<long>(type: "bigint", nullable: false),
                    CheckedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    Payload = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_player_watch", x => new { x.UserId, x.GameMode });
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "player_watch_event",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "int", nullable: false),
                    GameMode = table.Column<int>(type: "int", nullable: false),
                    Cursor = table.Column<long>(type: "bigint", nullable: false),
                    ObservedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    Text = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_player_watch_event", x => new { x.UserId, x.GameMode, x.Cursor });
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_player_subscription_UserId_GameMode_Scope",
                table: "player_subscription",
                columns: new[] { "UserId", "GameMode", "Scope" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "best_play_snapshot");

            migrationBuilder.DropTable(
                name: "player_subscription");

            migrationBuilder.DropTable(
                name: "player_watch");

            migrationBuilder.DropTable(
                name: "player_watch_event");
        }
    }
}
