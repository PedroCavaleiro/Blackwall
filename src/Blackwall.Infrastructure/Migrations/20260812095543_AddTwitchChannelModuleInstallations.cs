using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Blackwall.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTwitchChannelModuleInstallations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TwitchChannelModuleInstallations",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TwitchChannelInstanceId = table.Column<long>(type: "bigint", nullable: false),
                    ModuleName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ModuleVersion = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ModuleAuthor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    EntryPoint = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    GitUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    CanPerformActions = table.Column<bool>(type: "boolean", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    SettingsJson = table.Column<string>(type: "text", nullable: false),
                    ManifestJson = table.Column<string>(type: "text", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TwitchChannelModuleInstallations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TwitchChannelModuleInstallations_TwitchChannelInstances_Twi~",
                        column: x => x.TwitchChannelInstanceId,
                        principalTable: "TwitchChannelInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TwitchChannelModuleInstallations_TwitchChannelInstanceId_Mo~",
                table: "TwitchChannelModuleInstallations",
                columns: new[] { "TwitchChannelInstanceId", "ModuleName" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TwitchChannelModuleInstallations");
        }
    }
}
