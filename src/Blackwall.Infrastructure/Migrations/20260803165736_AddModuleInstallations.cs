using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Blackwall.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddModuleInstallations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GuildModuleInstallations",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GuildInstanceId = table.Column<long>(type: "bigint", nullable: false),
                    ModuleName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ModuleVersion = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ModuleAuthor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    EntryPoint = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CanPerformActions = table.Column<bool>(type: "boolean", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    SettingsJson = table.Column<string>(type: "text", nullable: false),
                    ManifestJson = table.Column<string>(type: "text", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GuildModuleInstallations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GuildModuleInstallations_GuildInstances_GuildInstanceId",
                        column: x => x.GuildInstanceId,
                        principalTable: "GuildInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GuildModuleInstallations_GuildInstanceId_ModuleName",
                table: "GuildModuleInstallations",
                columns: new[] { "GuildInstanceId", "ModuleName" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GuildModuleInstallations");
        }
    }
}
