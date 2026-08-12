using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Blackwall.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTwitchMessageAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsMessageAuditEnabled",
                table: "TwitchChannelConfigurations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "MessageAuditRetentionDays",
                table: "TwitchChannelConfigurations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "TwitchMessageAuditEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TwitchChannelInstanceId = table.Column<long>(type: "bigint", nullable: false),
                    TwitchUserId = table.Column<long>(type: "bigint", nullable: false),
                    Username = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Violations = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Action = table.Column<int>(type: "integer", nullable: false),
                    IsDryRun = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TwitchMessageAuditEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TwitchMessageAuditEvents_TwitchChannelInstances_TwitchChann~",
                        column: x => x.TwitchChannelInstanceId,
                        principalTable: "TwitchChannelInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TwitchMessageAuditRecords",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EventId = table.Column<long>(type: "bigint", nullable: false),
                    DiscordMessageId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    TwitchUserId = table.Column<long>(type: "bigint", nullable: false),
                    Username = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    MessageTimestampUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TwitchMessageAuditRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TwitchMessageAuditRecords_TwitchMessageAuditEvents_EventId",
                        column: x => x.EventId,
                        principalTable: "TwitchMessageAuditEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TwitchMessageAuditEvents_TwitchChannelInstanceId_CreatedAtU~",
                table: "TwitchMessageAuditEvents",
                columns: new[] { "TwitchChannelInstanceId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_TwitchMessageAuditRecords_EventId",
                table: "TwitchMessageAuditRecords",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_TwitchMessageAuditRecords_ExpiresAtUtc",
                table: "TwitchMessageAuditRecords",
                column: "ExpiresAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TwitchMessageAuditRecords");

            migrationBuilder.DropTable(
                name: "TwitchMessageAuditEvents");

            migrationBuilder.DropColumn(
                name: "IsMessageAuditEnabled",
                table: "TwitchChannelConfigurations");

            migrationBuilder.DropColumn(
                name: "MessageAuditRetentionDays",
                table: "TwitchChannelConfigurations");
        }
    }
}
