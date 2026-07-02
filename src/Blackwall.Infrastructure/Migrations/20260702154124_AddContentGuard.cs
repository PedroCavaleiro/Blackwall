using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Blackwall.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddContentGuard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ContentGuardAction",
                table: "SpamConfigurations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "ContentGuardAutoLockdown",
                table: "SpamConfigurations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ContentGuardCopypastaHashing",
                table: "SpamConfigurations",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "ContentGuardCopypastaMinLength",
                table: "SpamConfigurations",
                type: "integer",
                nullable: false,
                defaultValue: 200);

            migrationBuilder.AddColumn<int>(
                name: "ContentGuardCopypastaThreshold",
                table: "SpamConfigurations",
                type: "integer",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.AddColumn<int>(
                name: "ContentGuardCopypastaWindowSeconds",
                table: "SpamConfigurations",
                type: "integer",
                nullable: false,
                defaultValue: 60);

            migrationBuilder.AddColumn<bool>(
                name: "ContentGuardFuzzyMatching",
                table: "SpamConfigurations",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "ContentGuardFuzzyThreshold",
                table: "SpamConfigurations",
                type: "integer",
                nullable: false,
                defaultValue: 2);

            migrationBuilder.AddColumn<bool>(
                name: "ContentGuardInvisibleCharScrubbing",
                table: "SpamConfigurations",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "ContentGuardMessageDeleteDays",
                table: "SpamConfigurations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ContentGuardTimeoutMinutes",
                table: "SpamConfigurations",
                type: "integer",
                nullable: false,
                defaultValue: 10);

            migrationBuilder.AddColumn<bool>(
                name: "ContentGuardZalgoBlocking",
                table: "SpamConfigurations",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "ContentGuardZalgoMaxCombining",
                table: "SpamConfigurations",
                type: "integer",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.AddColumn<bool>(
                name: "IsContentGuardEnabled",
                table: "SpamConfigurations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "GuildBannedWords",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SpamConfigurationId = table.Column<long>(type: "bigint", nullable: false),
                    Word = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GuildBannedWords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GuildBannedWords_SpamConfigurations_SpamConfigurationId",
                        column: x => x.SpamConfigurationId,
                        principalTable: "SpamConfigurations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GuildBannedWords_SpamConfigurationId_Word",
                table: "GuildBannedWords",
                columns: new[] { "SpamConfigurationId", "Word" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GuildBannedWords");

            migrationBuilder.DropColumn(
                name: "ContentGuardAction",
                table: "SpamConfigurations");

            migrationBuilder.DropColumn(
                name: "ContentGuardAutoLockdown",
                table: "SpamConfigurations");

            migrationBuilder.DropColumn(
                name: "ContentGuardCopypastaHashing",
                table: "SpamConfigurations");

            migrationBuilder.DropColumn(
                name: "ContentGuardCopypastaMinLength",
                table: "SpamConfigurations");

            migrationBuilder.DropColumn(
                name: "ContentGuardCopypastaThreshold",
                table: "SpamConfigurations");

            migrationBuilder.DropColumn(
                name: "ContentGuardCopypastaWindowSeconds",
                table: "SpamConfigurations");

            migrationBuilder.DropColumn(
                name: "ContentGuardFuzzyMatching",
                table: "SpamConfigurations");

            migrationBuilder.DropColumn(
                name: "ContentGuardFuzzyThreshold",
                table: "SpamConfigurations");

            migrationBuilder.DropColumn(
                name: "ContentGuardInvisibleCharScrubbing",
                table: "SpamConfigurations");

            migrationBuilder.DropColumn(
                name: "ContentGuardMessageDeleteDays",
                table: "SpamConfigurations");

            migrationBuilder.DropColumn(
                name: "ContentGuardTimeoutMinutes",
                table: "SpamConfigurations");

            migrationBuilder.DropColumn(
                name: "ContentGuardZalgoBlocking",
                table: "SpamConfigurations");

            migrationBuilder.DropColumn(
                name: "ContentGuardZalgoMaxCombining",
                table: "SpamConfigurations");

            migrationBuilder.DropColumn(
                name: "IsContentGuardEnabled",
                table: "SpamConfigurations");
        }
    }
}
