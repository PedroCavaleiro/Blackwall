using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Blackwall.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSafeBrowsing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "SafeBrowsingBlockUnsure",
                table: "SpamConfigurations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "SafeBrowsingEnabled",
                table: "SpamConfigurations",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SafeBrowsingBlockUnsure",
                table: "SpamConfigurations");

            migrationBuilder.DropColumn(
                name: "SafeBrowsingEnabled",
                table: "SpamConfigurations");
        }
    }
}
