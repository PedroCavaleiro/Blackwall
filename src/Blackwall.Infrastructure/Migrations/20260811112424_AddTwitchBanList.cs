using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Blackwall.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTwitchBanList : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ShareBanList",
                table: "TwitchChannelInstances",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "TwitchChannelBans",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TwitchChannelInstanceId = table.Column<long>(type: "bigint", nullable: false),
                    TwitchUserId = table.Column<long>(type: "bigint", nullable: false),
                    Username = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    BannedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TwitchChannelBans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TwitchChannelBans_TwitchChannelInstances_TwitchChannelInsta~",
                        column: x => x.TwitchChannelInstanceId,
                        principalTable: "TwitchChannelInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TwitchChannelBanSyncRules",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TargetTwitchChannelInstanceId = table.Column<long>(type: "bigint", nullable: false),
                    SourceTwitchUserId = table.Column<long>(type: "bigint", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    LastSyncedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TwitchChannelBanSyncRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TwitchChannelBanSyncRules_TwitchChannelInstances_TargetTwit~",
                        column: x => x.TargetTwitchChannelInstanceId,
                        principalTable: "TwitchChannelInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TwitchChannelBans_TwitchChannelInstanceId_TwitchUserId",
                table: "TwitchChannelBans",
                columns: new[] { "TwitchChannelInstanceId", "TwitchUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TwitchChannelBanSyncRules_TargetTwitchChannelInstanceId_Sou~",
                table: "TwitchChannelBanSyncRules",
                columns: new[] { "TargetTwitchChannelInstanceId", "SourceTwitchUserId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TwitchChannelBans");

            migrationBuilder.DropTable(
                name: "TwitchChannelBanSyncRules");

            migrationBuilder.DropColumn(
                name: "ShareBanList",
                table: "TwitchChannelInstances");
        }
    }
}
