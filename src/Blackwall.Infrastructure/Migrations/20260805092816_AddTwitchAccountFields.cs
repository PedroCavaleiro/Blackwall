using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Blackwall.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTwitchAccountFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AppUsers_DiscordUserId",
                table: "AppUsers");

            migrationBuilder.AlterColumn<long>(
                name: "DiscordUserId",
                table: "AppUsers",
                type: "bigint",
                maxLength: 32,
                nullable: true,
                oldMaxLength: 32,
                oldNullable: false);

            migrationBuilder.AddColumn<string>(
                name: "ActiveDisplayNameProvider",
                table: "AppUsers",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TwitchAccessToken",
                table: "AppUsers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TwitchDisplayName",
                table: "AppUsers",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TwitchRefreshToken",
                table: "AppUsers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TwitchTokenExpiresAtUtc",
                table: "AppUsers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "TwitchUserId",
                table: "AppUsers",
                type: "bigint",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TwitchUsername",
                table: "AppUsers",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppUsers_DiscordUserId",
                table: "AppUsers",
                column: "DiscordUserId",
                unique: true,
                filter: "\"DiscordUserId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AppUsers_TwitchUserId",
                table: "AppUsers",
                column: "TwitchUserId",
                unique: true,
                filter: "\"TwitchUserId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AppUsers_DiscordUserId",
                table: "AppUsers");

            migrationBuilder.DropIndex(
                name: "IX_AppUsers_TwitchUserId",
                table: "AppUsers");

            migrationBuilder.DropColumn(
                name: "ActiveDisplayNameProvider",
                table: "AppUsers");

            migrationBuilder.DropColumn(
                name: "TwitchAccessToken",
                table: "AppUsers");

            migrationBuilder.DropColumn(
                name: "TwitchDisplayName",
                table: "AppUsers");

            migrationBuilder.DropColumn(
                name: "TwitchRefreshToken",
                table: "AppUsers");

            migrationBuilder.DropColumn(
                name: "TwitchTokenExpiresAtUtc",
                table: "AppUsers");

            migrationBuilder.DropColumn(
                name: "TwitchUserId",
                table: "AppUsers");

            migrationBuilder.DropColumn(
                name: "TwitchUsername",
                table: "AppUsers");

            migrationBuilder.AlterColumn<long>(
                name: "DiscordUserId",
                table: "AppUsers",
                type: "bigint",
                maxLength: 32,
                nullable: false,
                oldMaxLength: 32,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppUsers_DiscordUserId",
                table: "AppUsers",
                column: "DiscordUserId",
                unique: true);
        }
    }
}
