using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Blackwall.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLockdown : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsLockedDown",
                table: "SpamConfigurations",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsLockedDown",
                table: "SpamConfigurations");
        }
    }
}
