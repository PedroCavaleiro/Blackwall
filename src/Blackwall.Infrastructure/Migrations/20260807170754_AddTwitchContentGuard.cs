using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Blackwall.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTwitchContentGuard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ContentGuardAction",
                table: "TwitchChannelConfigurations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "ContentGuardFuzzyMatching",
                table: "TwitchChannelConfigurations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "ContentGuardFuzzyThreshold",
                table: "TwitchChannelConfigurations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ContentGuardTimeoutMinutes",
                table: "TwitchChannelConfigurations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "IsContentGuardEnabled",
                table: "TwitchChannelConfigurations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "TwitchChannelBannedWords",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TwitchChannelConfigurationId = table.Column<long>(type: "bigint", nullable: false),
                    Word = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IsRegex = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TwitchChannelBannedWords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TwitchChannelBannedWords_TwitchChannelConfigurations_Twitch~",
                        column: x => x.TwitchChannelConfigurationId,
                        principalTable: "TwitchChannelConfigurations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TwitchChannelBannedWords_TwitchChannelConfigurationId_Word",
                table: "TwitchChannelBannedWords",
                columns: new[] { "TwitchChannelConfigurationId", "Word" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TwitchChannelBannedWords");

            migrationBuilder.DropColumn(
                name: "ContentGuardAction",
                table: "TwitchChannelConfigurations");

            migrationBuilder.DropColumn(
                name: "ContentGuardFuzzyMatching",
                table: "TwitchChannelConfigurations");

            migrationBuilder.DropColumn(
                name: "ContentGuardFuzzyThreshold",
                table: "TwitchChannelConfigurations");

            migrationBuilder.DropColumn(
                name: "ContentGuardTimeoutMinutes",
                table: "TwitchChannelConfigurations");

            migrationBuilder.DropColumn(
                name: "IsContentGuardEnabled",
                table: "TwitchChannelConfigurations");
        }
    }
}
