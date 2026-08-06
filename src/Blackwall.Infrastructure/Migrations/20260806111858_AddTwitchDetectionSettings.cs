using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Blackwall.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTwitchDetectionSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DuplicateAction",
                table: "TwitchChannelConfigurations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "DuplicateMessageThreshold",
                table: "TwitchChannelConfigurations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "DuplicateTimeoutMinutes",
                table: "TwitchChannelConfigurations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "DuplicateWindowSeconds",
                table: "TwitchChannelConfigurations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MaxMessagesPerWindow",
                table: "TwitchChannelConfigurations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MentionLimit",
                table: "TwitchChannelConfigurations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MentionLimitAction",
                table: "TwitchChannelConfigurations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MentionLimitTimeoutMinutes",
                table: "TwitchChannelConfigurations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RateLimitAction",
                table: "TwitchChannelConfigurations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RateLimitTimeoutMinutes",
                table: "TwitchChannelConfigurations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RateLimitWindowSeconds",
                table: "TwitchChannelConfigurations",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DuplicateAction",
                table: "TwitchChannelConfigurations");

            migrationBuilder.DropColumn(
                name: "DuplicateMessageThreshold",
                table: "TwitchChannelConfigurations");

            migrationBuilder.DropColumn(
                name: "DuplicateTimeoutMinutes",
                table: "TwitchChannelConfigurations");

            migrationBuilder.DropColumn(
                name: "DuplicateWindowSeconds",
                table: "TwitchChannelConfigurations");

            migrationBuilder.DropColumn(
                name: "MaxMessagesPerWindow",
                table: "TwitchChannelConfigurations");

            migrationBuilder.DropColumn(
                name: "MentionLimit",
                table: "TwitchChannelConfigurations");

            migrationBuilder.DropColumn(
                name: "MentionLimitAction",
                table: "TwitchChannelConfigurations");

            migrationBuilder.DropColumn(
                name: "MentionLimitTimeoutMinutes",
                table: "TwitchChannelConfigurations");

            migrationBuilder.DropColumn(
                name: "RateLimitAction",
                table: "TwitchChannelConfigurations");

            migrationBuilder.DropColumn(
                name: "RateLimitTimeoutMinutes",
                table: "TwitchChannelConfigurations");

            migrationBuilder.DropColumn(
                name: "RateLimitWindowSeconds",
                table: "TwitchChannelConfigurations");
        }
    }
}
