using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Blackwall.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddModuleOverrides : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DuplicateAction",
                table: "SpamConfigurations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "DuplicateAutoLockdown",
                table: "SpamConfigurations",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "InviteLinkAction",
                table: "SpamConfigurations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "InviteLinkAutoLockdown",
                table: "SpamConfigurations",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MentionLimitAction",
                table: "SpamConfigurations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "MentionLimitAutoLockdown",
                table: "SpamConfigurations",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RateLimitAction",
                table: "SpamConfigurations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RateLimitAutoLockdown",
                table: "SpamConfigurations",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SuspiciousLinkAction",
                table: "SpamConfigurations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SuspiciousLinkAutoLockdown",
                table: "SpamConfigurations",
                type: "boolean",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DuplicateAction",
                table: "SpamConfigurations");

            migrationBuilder.DropColumn(
                name: "DuplicateAutoLockdown",
                table: "SpamConfigurations");

            migrationBuilder.DropColumn(
                name: "InviteLinkAction",
                table: "SpamConfigurations");

            migrationBuilder.DropColumn(
                name: "InviteLinkAutoLockdown",
                table: "SpamConfigurations");

            migrationBuilder.DropColumn(
                name: "MentionLimitAction",
                table: "SpamConfigurations");

            migrationBuilder.DropColumn(
                name: "MentionLimitAutoLockdown",
                table: "SpamConfigurations");

            migrationBuilder.DropColumn(
                name: "RateLimitAction",
                table: "SpamConfigurations");

            migrationBuilder.DropColumn(
                name: "RateLimitAutoLockdown",
                table: "SpamConfigurations");

            migrationBuilder.DropColumn(
                name: "SuspiciousLinkAction",
                table: "SpamConfigurations");

            migrationBuilder.DropColumn(
                name: "SuspiciousLinkAutoLockdown",
                table: "SpamConfigurations");
        }
    }
}
