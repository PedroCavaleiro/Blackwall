using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Blackwall.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBlacklistDomainsAndWhitelistMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "LinkWhitelistMode",
                table: "SpamConfigurations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "GuildBlacklistDomains",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SpamConfigurationId = table.Column<long>(type: "bigint", nullable: false),
                    Domain = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GuildBlacklistDomains", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GuildBlacklistDomains_SpamConfigurations_SpamConfigurationId",
                        column: x => x.SpamConfigurationId,
                        principalTable: "SpamConfigurations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GuildBlacklistDomains_SpamConfigurationId_Domain",
                table: "GuildBlacklistDomains",
                columns: new[] { "SpamConfigurationId", "Domain" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GuildBlacklistDomains");

            migrationBuilder.DropColumn(
                name: "LinkWhitelistMode",
                table: "SpamConfigurations");
        }
    }
}
