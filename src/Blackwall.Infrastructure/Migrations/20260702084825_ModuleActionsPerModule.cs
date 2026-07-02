using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Blackwall.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ModuleActionsPerModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<bool>(
                name: "SuspiciousLinkAutoLockdown",
                table: "SpamConfigurations",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "SuspiciousLinkAction",
                table: "SpamConfigurations",
                type: "integer",
                nullable: false,
                defaultValue: 1,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<bool>(
                name: "RateLimitAutoLockdown",
                table: "SpamConfigurations",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "RateLimitAction",
                table: "SpamConfigurations",
                type: "integer",
                nullable: false,
                defaultValue: 1,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<bool>(
                name: "MentionLimitAutoLockdown",
                table: "SpamConfigurations",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "MentionLimitAction",
                table: "SpamConfigurations",
                type: "integer",
                nullable: false,
                defaultValue: 1,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<bool>(
                name: "InviteLinkAutoLockdown",
                table: "SpamConfigurations",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "InviteLinkAction",
                table: "SpamConfigurations",
                type: "integer",
                nullable: false,
                defaultValue: 1,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<bool>(
                name: "DuplicateAutoLockdown",
                table: "SpamConfigurations",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "DuplicateAction",
                table: "SpamConfigurations",
                type: "integer",
                nullable: false,
                defaultValue: 1,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DuplicateMessageDeleteDays",
                table: "SpamConfigurations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "DuplicateTimeoutMinutes",
                table: "SpamConfigurations",
                type: "integer",
                nullable: false,
                defaultValue: 10);

            migrationBuilder.AddColumn<int>(
                name: "InviteLinkMessageDeleteDays",
                table: "SpamConfigurations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "InviteLinkTimeoutMinutes",
                table: "SpamConfigurations",
                type: "integer",
                nullable: false,
                defaultValue: 10);

            migrationBuilder.AddColumn<int>(
                name: "MentionLimitMessageDeleteDays",
                table: "SpamConfigurations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MentionLimitTimeoutMinutes",
                table: "SpamConfigurations",
                type: "integer",
                nullable: false,
                defaultValue: 10);

            migrationBuilder.AddColumn<int>(
                name: "RateLimitMessageDeleteDays",
                table: "SpamConfigurations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RateLimitTimeoutMinutes",
                table: "SpamConfigurations",
                type: "integer",
                nullable: false,
                defaultValue: 10);

            migrationBuilder.AddColumn<int>(
                name: "SuspiciousLinkMessageDeleteDays",
                table: "SpamConfigurations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SuspiciousLinkTimeoutMinutes",
                table: "SpamConfigurations",
                type: "integer",
                nullable: false,
                defaultValue: 10);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DuplicateMessageDeleteDays",
                table: "SpamConfigurations");

            migrationBuilder.DropColumn(
                name: "DuplicateTimeoutMinutes",
                table: "SpamConfigurations");

            migrationBuilder.DropColumn(
                name: "InviteLinkMessageDeleteDays",
                table: "SpamConfigurations");

            migrationBuilder.DropColumn(
                name: "InviteLinkTimeoutMinutes",
                table: "SpamConfigurations");

            migrationBuilder.DropColumn(
                name: "MentionLimitMessageDeleteDays",
                table: "SpamConfigurations");

            migrationBuilder.DropColumn(
                name: "MentionLimitTimeoutMinutes",
                table: "SpamConfigurations");

            migrationBuilder.DropColumn(
                name: "RateLimitMessageDeleteDays",
                table: "SpamConfigurations");

            migrationBuilder.DropColumn(
                name: "RateLimitTimeoutMinutes",
                table: "SpamConfigurations");

            migrationBuilder.DropColumn(
                name: "SuspiciousLinkMessageDeleteDays",
                table: "SpamConfigurations");

            migrationBuilder.DropColumn(
                name: "SuspiciousLinkTimeoutMinutes",
                table: "SpamConfigurations");

            migrationBuilder.AlterColumn<bool>(
                name: "SuspiciousLinkAutoLockdown",
                table: "SpamConfigurations",
                type: "boolean",
                nullable: true,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: false);

            migrationBuilder.AlterColumn<int>(
                name: "SuspiciousLinkAction",
                table: "SpamConfigurations",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 1);

            migrationBuilder.AlterColumn<bool>(
                name: "RateLimitAutoLockdown",
                table: "SpamConfigurations",
                type: "boolean",
                nullable: true,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: false);

            migrationBuilder.AlterColumn<int>(
                name: "RateLimitAction",
                table: "SpamConfigurations",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 1);

            migrationBuilder.AlterColumn<bool>(
                name: "MentionLimitAutoLockdown",
                table: "SpamConfigurations",
                type: "boolean",
                nullable: true,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: false);

            migrationBuilder.AlterColumn<int>(
                name: "MentionLimitAction",
                table: "SpamConfigurations",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 1);

            migrationBuilder.AlterColumn<bool>(
                name: "InviteLinkAutoLockdown",
                table: "SpamConfigurations",
                type: "boolean",
                nullable: true,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: false);

            migrationBuilder.AlterColumn<int>(
                name: "InviteLinkAction",
                table: "SpamConfigurations",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 1);

            migrationBuilder.AlterColumn<bool>(
                name: "DuplicateAutoLockdown",
                table: "SpamConfigurations",
                type: "boolean",
                nullable: true,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: false);

            migrationBuilder.AlterColumn<int>(
                name: "DuplicateAction",
                table: "SpamConfigurations",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 1);
        }
    }
}
