using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Blackwall.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddGuildAllowedBot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GuildAllowedBots",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SpamConfigurationId = table.Column<long>(type: "bigint", nullable: false),
                    DiscordBotId = table.Column<long>(type: "bigint", nullable: false),
                    BotUsername = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GuildAllowedBots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GuildAllowedBots_SpamConfigurations_SpamConfigurationId",
                        column: x => x.SpamConfigurationId,
                        principalTable: "SpamConfigurations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GuildAllowedBots_SpamConfigurationId_DiscordBotId",
                table: "GuildAllowedBots",
                columns: new[] { "SpamConfigurationId", "DiscordBotId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GuildAllowedBots");
        }
    }
}
