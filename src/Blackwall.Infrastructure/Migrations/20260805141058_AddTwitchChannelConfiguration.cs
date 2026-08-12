using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Blackwall.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTwitchChannelConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TwitchChannelConfigurations",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TwitchChannelInstanceId = table.Column<long>(type: "bigint", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    IsDryRun = table.Column<bool>(type: "boolean", nullable: false),
                    CommandTrigger = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TwitchChannelConfigurations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TwitchChannelConfigurations_TwitchChannelInstances_TwitchCh~",
                        column: x => x.TwitchChannelInstanceId,
                        principalTable: "TwitchChannelInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TwitchAllowedBots",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TwitchChannelConfigurationId = table.Column<long>(type: "bigint", nullable: false),
                    BotUsername = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TwitchAllowedBots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TwitchAllowedBots_TwitchChannelConfigurations_TwitchChannel~",
                        column: x => x.TwitchChannelConfigurationId,
                        principalTable: "TwitchChannelConfigurations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TwitchAllowedBots_TwitchChannelConfigurationId_BotUsername",
                table: "TwitchAllowedBots",
                columns: new[] { "TwitchChannelConfigurationId", "BotUsername" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TwitchChannelConfigurations_TwitchChannelInstanceId",
                table: "TwitchChannelConfigurations",
                column: "TwitchChannelInstanceId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TwitchAllowedBots");

            migrationBuilder.DropTable(
                name: "TwitchChannelConfigurations");
        }
    }
}
