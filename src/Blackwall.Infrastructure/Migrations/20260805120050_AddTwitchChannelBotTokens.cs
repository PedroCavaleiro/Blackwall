using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Blackwall.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTwitchChannelBotTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BotAccessToken",
                table: "TwitchChannelInstances",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BotRefreshToken",
                table: "TwitchChannelInstances",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "BotTokenExpiresAtUtc",
                table: "TwitchChannelInstances",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BotAccessToken",
                table: "TwitchChannelInstances");

            migrationBuilder.DropColumn(
                name: "BotRefreshToken",
                table: "TwitchChannelInstances");

            migrationBuilder.DropColumn(
                name: "BotTokenExpiresAtUtc",
                table: "TwitchChannelInstances");
        }
    }
}
