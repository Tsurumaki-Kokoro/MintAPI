using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MintAPI.Data.Migrations
{
    /// <inheritdoc />
    public partial class RefineHistoryAndSubscriptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Validate before permanent DDL: MySQL may otherwise coerce malformed IDs to zero.
            Guard(migrationBuilder, "Invalid or noncanonical osu_uid; inspect user and user_osu_info_history", """
                EXISTS (
                    SELECT 1 FROM (
                        SELECT osu_uid FROM user UNION ALL SELECT osu_uid FROM user_osu_info_history
                    ) ids
                    WHERE osu_uid REGEXP '[^0-9]' OR osu_uid = '' OR LEFT(osu_uid, 1) = '0'
                       OR CHAR_LENGTH(osu_uid) > 10
                       OR (CHAR_LENGTH(osu_uid) = 10 AND osu_uid COLLATE utf8mb4_bin > '2147483647')
                )
                """);

            // Match PlayerWatchGroup.Parse, including .NET whitespace trimming and UTF-16 limits.
            // Scope remains unchanged; these columns are its indexed group projection.
            const string whitespace = "\u0009\u000a\u000b\u000c\u000d\u0020\u0085\u00a0\u1680\u2000\u2001\u2002\u2003\u2004\u2005\u2006\u2007\u2008\u2009\u200a\u2028\u2029\u202f\u205f\u3000";
            var whitespaceHex = Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes(whitespace));
            migrationBuilder.Sql($"""
                DROP TEMPORARY TABLE IF EXISTS mintapi_group_backfill;
                CREATE TEMPORARY TABLE mintapi_group_backfill AS
                SELECT Id, UserId, GameMode, LOWER(platform_value) COLLATE utf8mb4_bin AS Platform,
                       group_value COLLATE utf8mb4_bin AS GroupId
                FROM (
                    SELECT Id, UserId, GameMode,
                        REGEXP_REPLACE(LEFT(Scope, LOCATE(':group:', Scope COLLATE utf8mb4_bin) - 1),
                            CONCAT('^[', CONVERT(0x{whitespaceHex} USING utf8mb4), ']+|[', CONVERT(0x{whitespaceHex} USING utf8mb4), ']+$'), '') AS platform_value,
                        REGEXP_REPLACE(SUBSTRING(Scope, LOCATE(':group:', Scope COLLATE utf8mb4_bin) + 7),
                            CONCAT('^[', CONVERT(0x{whitespaceHex} USING utf8mb4), ']+|[', CONVERT(0x{whitespaceHex} USING utf8mb4), ']+$'), '') AS group_value
                    FROM player_subscription
                    WHERE LOCATE(':group:', Scope COLLATE utf8mb4_bin) > 1
                ) parsed
                WHERE platform_value <> '' AND CHAR_LENGTH(platform_value) <= 32
                  AND LOWER(platform_value) COLLATE utf8mb4_bin NOT REGEXP '[^a-z0-9_-]'
                  AND group_value <> '' AND LENGTH(CONVERT(group_value USING utf16)) <= 160
                  AND group_value NOT REGEXP CONCAT('[', CONVERT(0x{whitespaceHex} USING utf8mb4), ']')
                  AND group_value NOT REGEXP '[[:cntrl:]]';
                """);
            Guard(migrationBuilder, "Duplicate normalized group subscriptions; resolve aliases before upgrading", """
                EXISTS (SELECT 1 FROM mintapi_group_backfill
                    GROUP BY UserId, GameMode, Platform, GroupId HAVING COUNT(*) > 1)
                """);

            migrationBuilder.AlterColumn<long>(
                name: "total_hits",
                table: "user_osu_info_history",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "osu_uid",
                table: "user_osu_info_history",
                type: "int",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(255)",
                oldMaxLength: 255)
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "observed_at",
                table: "user_osu_info_history",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ranked_score",
                table: "user_osu_info_history",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "total_score",
                table: "user_osu_info_history",
                type: "bigint",
                nullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "osu_uid",
                table: "user",
                type: "int",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(255)",
                oldMaxLength: 255)
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CollectedAt",
                table: "score_history",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Scope",
                table: "player_subscription",
                type: "varchar(128)",
                maxLength: 128,
                nullable: false,
                collation: "utf8mb4_bin",
                oldClrType: typeof(string),
                oldType: "varchar(128)",
                oldMaxLength: 128)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "GroupId",
                table: "player_subscription",
                type: "varchar(80)",
                maxLength: 80,
                nullable: true,
                collation: "utf8mb4_bin")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "Platform",
                table: "player_subscription",
                type: "varchar(32)",
                maxLength: 32,
                nullable: true,
                collation: "utf8mb4_bin")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.Sql("""
                UPDATE player_subscription subscription
                INNER JOIN mintapi_group_backfill parsed ON subscription.Id = parsed.Id
                SET subscription.Platform = parsed.Platform, subscription.GroupId = parsed.GroupId;
                DROP TEMPORARY TABLE mintapi_group_backfill;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_player_watch_event_UserId_GameMode_ObservedAt",
                table: "player_watch_event",
                columns: new[] { "UserId", "GameMode", "ObservedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_player_subscription_ExpiresAt",
                table: "player_subscription",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_player_subscription_Platform_GroupId_UserId_GameMode",
                table: "player_subscription",
                columns: new[] { "Platform", "GroupId", "UserId", "GameMode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_player_subscription_Scope_UserId_GameMode",
                table: "player_subscription",
                columns: new[] { "Scope", "UserId", "GameMode" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            Guard(migrationBuilder, "total_hits exceeds int range; downgrade would lose data", """
                EXISTS (SELECT 1 FROM user_osu_info_history
                    WHERE total_hits > 2147483647 OR total_hits < -2147483648)
                """);
            // Recreate the old column's comparison rules before dropping any new data.
            migrationBuilder.Sql("""
                DROP TEMPORARY TABLE IF EXISTS mintapi_scope_downgrade_guard;
                CREATE TEMPORARY TABLE mintapi_scope_downgrade_guard (
                    UserId int NOT NULL, GameMode int NOT NULL,
                    Scope varchar(128) CHARACTER SET utf8mb4 NOT NULL,
                    PRIMARY KEY (UserId, GameMode, Scope)
                );
                INSERT INTO mintapi_scope_downgrade_guard SELECT UserId, GameMode, Scope FROM player_subscription;
                DROP TEMPORARY TABLE mintapi_scope_downgrade_guard;
                """);

            migrationBuilder.DropIndex(
                name: "IX_player_watch_event_UserId_GameMode_ObservedAt",
                table: "player_watch_event");

            migrationBuilder.DropIndex(
                name: "IX_player_subscription_ExpiresAt",
                table: "player_subscription");

            migrationBuilder.DropIndex(
                name: "IX_player_subscription_Platform_GroupId_UserId_GameMode",
                table: "player_subscription");

            migrationBuilder.DropIndex(
                name: "IX_player_subscription_Scope_UserId_GameMode",
                table: "player_subscription");

            migrationBuilder.DropColumn(
                name: "observed_at",
                table: "user_osu_info_history");

            migrationBuilder.DropColumn(
                name: "ranked_score",
                table: "user_osu_info_history");

            migrationBuilder.DropColumn(
                name: "total_score",
                table: "user_osu_info_history");

            migrationBuilder.DropColumn(
                name: "CollectedAt",
                table: "score_history");

            migrationBuilder.DropColumn(
                name: "GroupId",
                table: "player_subscription");

            migrationBuilder.DropColumn(
                name: "Platform",
                table: "player_subscription");

            migrationBuilder.AlterColumn<int>(
                name: "total_hits",
                table: "user_osu_info_history",
                type: "int",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "osu_uid",
                table: "user_osu_info_history",
                type: "varchar(255)",
                maxLength: 255,
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<string>(
                name: "osu_uid",
                table: "user",
                type: "varchar(255)",
                maxLength: 255,
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<string>(
                name: "Scope",
                table: "player_subscription",
                type: "varchar(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(128)",
                oldMaxLength: 128,
                oldCollation: "utf8mb4_bin")
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");
        }
        private static void Guard(MigrationBuilder migrationBuilder, string reason, string condition)
        {
            // A duplicate primary key aborts even if the server's SQL mode permits coercion.
            migrationBuilder.Sql($"""
                DROP TEMPORARY TABLE IF EXISTS mintapi_schema_guard;
                CREATE TEMPORARY TABLE mintapi_schema_guard (Reason varchar(128) PRIMARY KEY);
                INSERT INTO mintapi_schema_guard VALUES ('{reason}');
                INSERT INTO mintapi_schema_guard SELECT '{reason}' WHERE {condition};
                DROP TEMPORARY TABLE mintapi_schema_guard;
                """);
        }
    }
}
