using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Blackwall.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAutoLockdown : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AutoLockdownEnabled",
                table: "SpamConfigurations",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AutoLockdownEnabled",
                table: "SpamConfigurations");
        }
    }
}
