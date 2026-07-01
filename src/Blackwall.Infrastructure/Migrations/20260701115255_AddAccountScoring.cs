using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Blackwall.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountScoring : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AccountScoringTimeoutMinutes",
                table: "SpamConfigurations",
                type: "integer",
                nullable: false,
                defaultValue: 10);

            migrationBuilder.AddColumn<bool>(
                name: "AutoTimeoutHighRiskOnJoin",
                table: "SpamConfigurations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AutoTimeoutMediumRiskOnJoin",
                table: "SpamConfigurations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsAccountScoringEnabled",
                table: "SpamConfigurations",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AccountScoringTimeoutMinutes",
                table: "SpamConfigurations");

            migrationBuilder.DropColumn(
                name: "AutoTimeoutHighRiskOnJoin",
                table: "SpamConfigurations");

            migrationBuilder.DropColumn(
                name: "AutoTimeoutMediumRiskOnJoin",
                table: "SpamConfigurations");

            migrationBuilder.DropColumn(
                name: "IsAccountScoringEnabled",
                table: "SpamConfigurations");
        }
    }
}
