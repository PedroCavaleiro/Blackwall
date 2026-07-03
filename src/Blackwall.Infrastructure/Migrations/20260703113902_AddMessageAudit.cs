using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Blackwall.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMessageAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsMessageAuditEnabled",
                table: "SpamConfigurations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "MessageAuditRetentionDays",
                table: "SpamConfigurations",
                type: "integer",
                nullable: false,
                defaultValue: 30);

            migrationBuilder.CreateTable(
                name: "MessageAuditEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GuildInstanceId = table.Column<long>(type: "bigint", nullable: false),
                    DiscordUserId = table.Column<long>(type: "bigint", nullable: false),
                    Username = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AvatarHash = table.Column<string>(type: "text", nullable: true),
                    DiscordChannelId = table.Column<long>(type: "bigint", nullable: false),
                    ChannelName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Violations = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Action = table.Column<int>(type: "integer", nullable: false),
                    IsDryRun = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MessageAuditEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MessageAuditEvents_GuildInstances_GuildInstanceId",
                        column: x => x.GuildInstanceId,
                        principalTable: "GuildInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MessageAuditRecords",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EventId = table.Column<long>(type: "bigint", nullable: false),
                    DiscordMessageId = table.Column<long>(type: "bigint", nullable: false),
                    DiscordUserId = table.Column<long>(type: "bigint", nullable: false),
                    Username = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AvatarHash = table.Column<string>(type: "text", nullable: true),
                    DiscordChannelId = table.Column<long>(type: "bigint", nullable: false),
                    ChannelName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    EmbedsJson = table.Column<string>(type: "text", nullable: false),
                    MessageTimestampUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MessageAuditRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MessageAuditRecords_MessageAuditEvents_EventId",
                        column: x => x.EventId,
                        principalTable: "MessageAuditEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MessageAuditEvents_GuildInstanceId_CreatedAtUtc",
                table: "MessageAuditEvents",
                columns: new[] { "GuildInstanceId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_MessageAuditRecords_EventId",
                table: "MessageAuditRecords",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_MessageAuditRecords_ExpiresAtUtc",
                table: "MessageAuditRecords",
                column: "ExpiresAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MessageAuditRecords");

            migrationBuilder.DropTable(
                name: "MessageAuditEvents");

            migrationBuilder.DropColumn(
                name: "IsMessageAuditEnabled",
                table: "SpamConfigurations");

            migrationBuilder.DropColumn(
                name: "MessageAuditRetentionDays",
                table: "SpamConfigurations");
        }
    }
}
