using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Blackwall.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAntiRaidOptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AntiRaidCooldownMinutes",
                table: "SpamConfigurations",
                type: "integer",
                nullable: false,
                defaultValue: 30);

            migrationBuilder.AddColumn<int>(
                name: "AntiRaidJoinThreshold",
                table: "SpamConfigurations",
                type: "integer",
                nullable: false,
                defaultValue: 10);

            migrationBuilder.AddColumn<int>(
                name: "AntiRaidWindowSeconds",
                table: "SpamConfigurations",
                type: "integer",
                nullable: false,
                defaultValue: 30);

            migrationBuilder.AddColumn<bool>(
                name: "IsAntiRaidEnabled",
                table: "SpamConfigurations",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AntiRaidCooldownMinutes",
                table: "SpamConfigurations");

            migrationBuilder.DropColumn(
                name: "AntiRaidJoinThreshold",
                table: "SpamConfigurations");

            migrationBuilder.DropColumn(
                name: "AntiRaidWindowSeconds",
                table: "SpamConfigurations");

            migrationBuilder.DropColumn(
                name: "IsAntiRaidEnabled",
                table: "SpamConfigurations");
        }
    }
}
