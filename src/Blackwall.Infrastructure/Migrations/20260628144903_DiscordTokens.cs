using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Blackwall.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DiscordTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DiscordAccessToken",
                table: "AppUsers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DiscordRefreshToken",
                table: "AppUsers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DiscordTokenExpiresAtUtc",
                table: "AppUsers",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DiscordAccessToken",
                table: "AppUsers");

            migrationBuilder.DropColumn(
                name: "DiscordRefreshToken",
                table: "AppUsers");

            migrationBuilder.DropColumn(
                name: "DiscordTokenExpiresAtUtc",
                table: "AppUsers");
        }
    }
}
