using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Blackwall.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTwitchLinkModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "BlockSuspiciousLinks",
                table: "TwitchChannelConfigurations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "LinkWhitelistMode",
                table: "TwitchChannelConfigurations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "SafeBrowsingBlockUnsure",
                table: "TwitchChannelConfigurations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "SafeBrowsingEnabled",
                table: "TwitchChannelConfigurations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "SuspiciousLinkAction",
                table: "TwitchChannelConfigurations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SuspiciousLinkTimeoutMinutes",
                table: "TwitchChannelConfigurations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "TwitchChannelBlacklists",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TwitchChannelConfigurationId = table.Column<long>(type: "bigint", nullable: false),
                    Url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TwitchChannelBlacklists", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TwitchChannelBlacklists_TwitchChannelConfigurations_TwitchC~",
                        column: x => x.TwitchChannelConfigurationId,
                        principalTable: "TwitchChannelConfigurations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TwitchChannelDomainRules",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TwitchChannelConfigurationId = table.Column<long>(type: "bigint", nullable: false),
                    Rule = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TwitchChannelDomainRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TwitchChannelDomainRules_TwitchChannelConfigurations_Twitch~",
                        column: x => x.TwitchChannelConfigurationId,
                        principalTable: "TwitchChannelConfigurations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TwitchChannelBlacklists_TwitchChannelConfigurationId_Url",
                table: "TwitchChannelBlacklists",
                columns: new[] { "TwitchChannelConfigurationId", "Url" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TwitchChannelDomainRules_TwitchChannelConfigurationId_Rule",
                table: "TwitchChannelDomainRules",
                columns: new[] { "TwitchChannelConfigurationId", "Rule" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TwitchChannelBlacklists");

            migrationBuilder.DropTable(
                name: "TwitchChannelDomainRules");

            migrationBuilder.DropColumn(
                name: "BlockSuspiciousLinks",
                table: "TwitchChannelConfigurations");

            migrationBuilder.DropColumn(
                name: "LinkWhitelistMode",
                table: "TwitchChannelConfigurations");

            migrationBuilder.DropColumn(
                name: "SafeBrowsingBlockUnsure",
                table: "TwitchChannelConfigurations");

            migrationBuilder.DropColumn(
                name: "SafeBrowsingEnabled",
                table: "TwitchChannelConfigurations");

            migrationBuilder.DropColumn(
                name: "SuspiciousLinkAction",
                table: "TwitchChannelConfigurations");

            migrationBuilder.DropColumn(
                name: "SuspiciousLinkTimeoutMinutes",
                table: "TwitchChannelConfigurations");
        }
    }
}
