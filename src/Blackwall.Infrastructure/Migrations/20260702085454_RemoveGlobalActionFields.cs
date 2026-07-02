using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Blackwall.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveGlobalActionFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Action",
                table: "SpamConfigurations");

            migrationBuilder.DropColumn(
                name: "AutoLockdownEnabled",
                table: "SpamConfigurations");

            migrationBuilder.DropColumn(
                name: "MessageDeleteDays",
                table: "SpamConfigurations");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Action",
                table: "SpamConfigurations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "AutoLockdownEnabled",
                table: "SpamConfigurations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "MessageDeleteDays",
                table: "SpamConfigurations",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }
    }
}
