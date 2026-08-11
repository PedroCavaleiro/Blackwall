using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Blackwall.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveAiSentinel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiSentinelLogs");

            migrationBuilder.DropTable(
                name: "AiSentinelConfigurations");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiSentinelConfigurations",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GuildInstanceId = table.Column<long>(type: "bigint", nullable: false),
                    Action = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    ApiKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    AutoLockdown = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDryRun = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    IsTrainingMode = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    MessageDeleteDays = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    Model = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    OllamaHeader1Key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    OllamaHeader1Value = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    OllamaHeader2Key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    OllamaHeader2Value = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    OllamaHeader3Key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    OllamaHeader3Value = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    OllamaUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Provider = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    TimeoutMinutes = table.Column<int>(type: "integer", nullable: false, defaultValue: 10),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiSentinelConfigurations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiSentinelConfigurations_GuildInstances_GuildInstanceId",
                        column: x => x.GuildInstanceId,
                        principalTable: "GuildInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AiSentinelLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AiSentinelConfigurationId = table.Column<long>(type: "bigint", nullable: false),
                    AvatarHash = table.Column<string>(type: "text", nullable: true),
                    ChannelName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Classification = table.Column<int>(type: "integer", nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DiscordChannelId = table.Column<long>(type: "bigint", nullable: false),
                    DiscordMessageId = table.Column<long>(type: "bigint", nullable: false),
                    DiscordUserId = table.Column<long>(type: "bigint", nullable: false),
                    EmbedsJson = table.Column<string>(type: "text", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDryRun = table.Column<bool>(type: "boolean", nullable: false),
                    MessageTimestampUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Model = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Provider = table.Column<int>(type: "integer", nullable: false),
                    Reasoning = table.Column<string>(type: "text", nullable: false),
                    TrainingFeedback = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    Username = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    WouldAction = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiSentinelLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiSentinelLogs_AiSentinelConfigurations_AiSentinelConfigura~",
                        column: x => x.AiSentinelConfigurationId,
                        principalTable: "AiSentinelConfigurations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiSentinelConfigurations_GuildInstanceId",
                table: "AiSentinelConfigurations",
                column: "GuildInstanceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AiSentinelLogs_AiSentinelConfigurationId",
                table: "AiSentinelLogs",
                column: "AiSentinelConfigurationId");

            migrationBuilder.CreateIndex(
                name: "IX_AiSentinelLogs_AiSentinelConfigurationId_CreatedAtUtc",
                table: "AiSentinelLogs",
                columns: new[] { "AiSentinelConfigurationId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AiSentinelLogs_ExpiresAtUtc",
                table: "AiSentinelLogs",
                column: "ExpiresAtUtc");
        }
    }
}
