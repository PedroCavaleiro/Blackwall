using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Blackwall.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MakeGuildInstanceOwnerNullable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_GuildInstances_AppUsers_OwnerUserId",
                table: "GuildInstances");

            migrationBuilder.AlterColumn<long>(
                name: "OwnerUserId",
                table: "GuildInstances",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AddForeignKey(
                name: "FK_GuildInstances_AppUsers_OwnerUserId",
                table: "GuildInstances",
                column: "OwnerUserId",
                principalTable: "AppUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_GuildInstances_AppUsers_OwnerUserId",
                table: "GuildInstances");

            migrationBuilder.AlterColumn<long>(
                name: "OwnerUserId",
                table: "GuildInstances",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_GuildInstances_AppUsers_OwnerUserId",
                table: "GuildInstances",
                column: "OwnerUserId",
                principalTable: "AppUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
