using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Blackwall.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddGuildInstanceIsActive : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_GuildInstance_AppUsers_OwnerUserId",
                table: "GuildInstance");

            migrationBuilder.DropForeignKey(
                name: "FK_GuildManager_AppUsers_UserId",
                table: "GuildManager");

            migrationBuilder.DropForeignKey(
                name: "FK_GuildManager_GuildInstance_GuildInstanceId",
                table: "GuildManager");

            migrationBuilder.DropForeignKey(
                name: "FK_spam_configurations_GuildInstance_GuildInstanceId",
                table: "spam_configurations");

            migrationBuilder.DropPrimaryKey(
                name: "PK_spam_configurations",
                table: "spam_configurations");

            migrationBuilder.DropPrimaryKey(
                name: "PK_GuildManager",
                table: "GuildManager");

            migrationBuilder.DropPrimaryKey(
                name: "PK_GuildInstance",
                table: "GuildInstance");

            migrationBuilder.RenameTable(
                name: "spam_configurations",
                newName: "SpamConfigurations");

            migrationBuilder.RenameTable(
                name: "GuildManager",
                newName: "GuildManagers");

            migrationBuilder.RenameTable(
                name: "GuildInstance",
                newName: "GuildInstances");

            migrationBuilder.RenameIndex(
                name: "IX_spam_configurations_GuildInstanceId",
                table: "SpamConfigurations",
                newName: "IX_SpamConfigurations_GuildInstanceId");

            migrationBuilder.RenameIndex(
                name: "IX_GuildManager_UserId",
                table: "GuildManagers",
                newName: "IX_GuildManagers_UserId");

            migrationBuilder.RenameIndex(
                name: "IX_GuildManager_GuildInstanceId_UserId_DiscordRoleId",
                table: "GuildManagers",
                newName: "IX_GuildManagers_GuildInstanceId_UserId_DiscordRoleId");

            migrationBuilder.RenameIndex(
                name: "IX_GuildInstance_OwnerUserId",
                table: "GuildInstances",
                newName: "IX_GuildInstances_OwnerUserId");

            migrationBuilder.RenameIndex(
                name: "IX_GuildInstance_DiscordGuildId",
                table: "GuildInstances",
                newName: "IX_GuildInstances_DiscordGuildId");

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "GuildInstances",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddPrimaryKey(
                name: "PK_SpamConfigurations",
                table: "SpamConfigurations",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_GuildManagers",
                table: "GuildManagers",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_GuildInstances",
                table: "GuildInstances",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_GuildInstances_AppUsers_OwnerUserId",
                table: "GuildInstances",
                column: "OwnerUserId",
                principalTable: "AppUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_GuildManagers_AppUsers_UserId",
                table: "GuildManagers",
                column: "UserId",
                principalTable: "AppUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_GuildManagers_GuildInstances_GuildInstanceId",
                table: "GuildManagers",
                column: "GuildInstanceId",
                principalTable: "GuildInstances",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_SpamConfigurations_GuildInstances_GuildInstanceId",
                table: "SpamConfigurations",
                column: "GuildInstanceId",
                principalTable: "GuildInstances",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_GuildInstances_AppUsers_OwnerUserId",
                table: "GuildInstances");

            migrationBuilder.DropForeignKey(
                name: "FK_GuildManagers_AppUsers_UserId",
                table: "GuildManagers");

            migrationBuilder.DropForeignKey(
                name: "FK_GuildManagers_GuildInstances_GuildInstanceId",
                table: "GuildManagers");

            migrationBuilder.DropForeignKey(
                name: "FK_SpamConfigurations_GuildInstances_GuildInstanceId",
                table: "SpamConfigurations");

            migrationBuilder.DropPrimaryKey(
                name: "PK_SpamConfigurations",
                table: "SpamConfigurations");

            migrationBuilder.DropPrimaryKey(
                name: "PK_GuildManagers",
                table: "GuildManagers");

            migrationBuilder.DropPrimaryKey(
                name: "PK_GuildInstances",
                table: "GuildInstances");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "GuildInstances");

            migrationBuilder.RenameTable(
                name: "SpamConfigurations",
                newName: "spam_configurations");

            migrationBuilder.RenameTable(
                name: "GuildManagers",
                newName: "GuildManager");

            migrationBuilder.RenameTable(
                name: "GuildInstances",
                newName: "GuildInstance");

            migrationBuilder.RenameIndex(
                name: "IX_SpamConfigurations_GuildInstanceId",
                table: "spam_configurations",
                newName: "IX_spam_configurations_GuildInstanceId");

            migrationBuilder.RenameIndex(
                name: "IX_GuildManagers_UserId",
                table: "GuildManager",
                newName: "IX_GuildManager_UserId");

            migrationBuilder.RenameIndex(
                name: "IX_GuildManagers_GuildInstanceId_UserId_DiscordRoleId",
                table: "GuildManager",
                newName: "IX_GuildManager_GuildInstanceId_UserId_DiscordRoleId");

            migrationBuilder.RenameIndex(
                name: "IX_GuildInstances_OwnerUserId",
                table: "GuildInstance",
                newName: "IX_GuildInstance_OwnerUserId");

            migrationBuilder.RenameIndex(
                name: "IX_GuildInstances_DiscordGuildId",
                table: "GuildInstance",
                newName: "IX_GuildInstance_DiscordGuildId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_spam_configurations",
                table: "spam_configurations",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_GuildManager",
                table: "GuildManager",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_GuildInstance",
                table: "GuildInstance",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_GuildInstance_AppUsers_OwnerUserId",
                table: "GuildInstance",
                column: "OwnerUserId",
                principalTable: "AppUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_GuildManager_AppUsers_UserId",
                table: "GuildManager",
                column: "UserId",
                principalTable: "AppUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_GuildManager_GuildInstance_GuildInstanceId",
                table: "GuildManager",
                column: "GuildInstanceId",
                principalTable: "GuildInstance",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_spam_configurations_GuildInstance_GuildInstanceId",
                table: "spam_configurations",
                column: "GuildInstanceId",
                principalTable: "GuildInstance",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
