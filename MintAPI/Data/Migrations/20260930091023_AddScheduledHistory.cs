using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MintAPI.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddScheduledHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_user_osu_info_history_osu_uid_date",
                table: "user_osu_info_history");

            migrationBuilder.CreateTable(
                name: "history_task_run",
                columns: table => new
                {
                    Job = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    FailedTargets = table.Column<int>(type: "int", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_history_task_run", x => new { x.Job, x.Date });
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "score_history",
                columns: table => new
                {
                    ScoreId = table.Column<long>(type: "bigint", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    BeatmapId = table.Column<int>(type: "int", nullable: false),
                    GameMode = table.Column<int>(type: "int", nullable: false),
                    EndedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    Payload = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_score_history", x => x.ScoreId);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            // Keep the newest snapshot when upgrading the old non-unique table.
            migrationBuilder.Sql("""
                DELETE older FROM user_osu_info_history older
                INNER JOIN user_osu_info_history newer
                  ON older.osu_uid = newer.osu_uid AND older.game_mode = newer.game_mode
                  AND older.date = newer.date AND older.id < newer.id;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_user_osu_info_history_osu_uid_game_mode_date",
                table: "user_osu_info_history",
                columns: new[] { "osu_uid", "game_mode", "date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_score_history_UserId_BeatmapId_GameMode_EndedAt",
                table: "score_history",
                columns: new[] { "UserId", "BeatmapId", "GameMode", "EndedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "history_task_run");

            migrationBuilder.DropTable(
                name: "score_history");

            migrationBuilder.DropIndex(
                name: "IX_user_osu_info_history_osu_uid_game_mode_date",
                table: "user_osu_info_history");

            migrationBuilder.CreateIndex(
                name: "IX_user_osu_info_history_osu_uid_date",
                table: "user_osu_info_history",
                columns: new[] { "osu_uid", "date" });
        }
    }
}
