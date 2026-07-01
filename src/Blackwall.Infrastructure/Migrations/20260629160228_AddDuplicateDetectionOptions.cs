using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Blackwall.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDuplicateDetectionOptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "DuplicateCrossChannelEnabled",
                table: "SpamConfigurations",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "DuplicateWindowSeconds",
                table: "SpamConfigurations",
                type: "integer",
                nullable: false,
                defaultValue: 5);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DuplicateCrossChannelEnabled",
                table: "SpamConfigurations");

            migrationBuilder.DropColumn(
                name: "DuplicateWindowSeconds",
                table: "SpamConfigurations");
        }
    }
}
