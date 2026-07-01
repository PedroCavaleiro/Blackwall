using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Blackwall.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSpamConfigurationOptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Action",
                table: "SpamConfigurations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "IsDryRun",
                table: "SpamConfigurations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsEnabled",
                table: "SpamConfigurations",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<long>(
                name: "LogChannelId",
                table: "SpamConfigurations",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MessageDeleteDays",
                table: "SpamConfigurations",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Action",
                table: "SpamConfigurations");

            migrationBuilder.DropColumn(
                name: "IsDryRun",
                table: "SpamConfigurations");

            migrationBuilder.DropColumn(
                name: "IsEnabled",
                table: "SpamConfigurations");

            migrationBuilder.DropColumn(
                name: "LogChannelId",
                table: "SpamConfigurations");

            migrationBuilder.DropColumn(
                name: "MessageDeleteDays",
                table: "SpamConfigurations");
        }
    }
}
