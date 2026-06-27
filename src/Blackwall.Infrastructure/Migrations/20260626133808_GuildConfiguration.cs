using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Blackwall.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class GuildConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GuildInstance",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DiscordGuildId = table.Column<long>(type: "bigint", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IconHash = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    OwnerUserId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GuildInstance", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GuildInstance_AppUsers_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GuildManager",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GuildInstanceId = table.Column<long>(type: "bigint", nullable: false),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    DiscordRoleId = table.Column<long>(type: "bigint", maxLength: 32, nullable: false),
                    IsAdmin = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GuildManager", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GuildManager_AppUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GuildManager_GuildInstance_GuildInstanceId",
                        column: x => x.GuildInstanceId,
                        principalTable: "GuildInstance",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "spam_configurations",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GuildInstanceId = table.Column<long>(type: "bigint", nullable: false),
                    MaxMessagesPerWindow = table.Column<int>(type: "integer", nullable: false),
                    RateLimitWindowSeconds = table.Column<int>(type: "integer", nullable: false),
                    DuplicateMessageThreshold = table.Column<int>(type: "integer", nullable: false),
                    MentionLimit = table.Column<int>(type: "integer", nullable: false),
                    BlockInviteLinks = table.Column<bool>(type: "boolean", nullable: false),
                    BlockSuspiciousLinks = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_spam_configurations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_spam_configurations_GuildInstance_GuildInstanceId",
                        column: x => x.GuildInstanceId,
                        principalTable: "GuildInstance",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GuildInstance_DiscordGuildId",
                table: "GuildInstance",
                column: "DiscordGuildId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GuildInstance_OwnerUserId",
                table: "GuildInstance",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_GuildManager_GuildInstanceId_UserId_DiscordRoleId",
                table: "GuildManager",
                columns: new[] { "GuildInstanceId", "UserId", "DiscordRoleId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GuildManager_UserId",
                table: "GuildManager",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_spam_configurations_GuildInstanceId",
                table: "spam_configurations",
                column: "GuildInstanceId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GuildManager");

            migrationBuilder.DropTable(
                name: "spam_configurations");

            migrationBuilder.DropTable(
                name: "GuildInstance");
        }
    }
}
