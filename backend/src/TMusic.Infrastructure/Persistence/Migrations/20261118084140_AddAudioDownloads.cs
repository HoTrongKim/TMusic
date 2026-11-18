using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TMusic.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAudioDownloads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AudioFileName",
                table: "UserLibrarySongs",
                type: "varchar(128)",
                unicode: false,
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "AudioSizeBytes",
                table: "UserLibrarySongs",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AudioStatus",
                table: "UserLibrarySongs",
                type: "varchar(24)",
                unicode: false,
                maxLength: 24,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AudioDownloadJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserLibrarySongId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Provider = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false, collation: "Latin1_General_100_BIN2"),
                    CanonicalUrl = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    RightsConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    ProgressPercent = table.Column<int>(type: "int", nullable: false),
                    OutputFileName = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: true),
                    OutputSizeBytes = table.Column<long>(type: "bigint", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    OwnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    ErrorCode = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    LeaseOwner = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LeaseUntil = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    HeartbeatAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Failures = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AudioDownloadJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AudioDownloadJobs_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AudioDownloadJobs_OwnerId_CreatedAt",
                table: "AudioDownloadJobs",
                columns: new[] { "OwnerId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AudioDownloadJobs_OwnerId_UserLibrarySongId",
                table: "AudioDownloadJobs",
                columns: new[] { "OwnerId", "UserLibrarySongId" });

            migrationBuilder.CreateIndex(
                name: "IX_AudioDownloadJobs_Status_NextAttemptAt_LeaseUntil",
                table: "AudioDownloadJobs",
                columns: new[] { "Status", "NextAttemptAt", "LeaseUntil" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AudioDownloadJobs");

            migrationBuilder.DropColumn(
                name: "AudioFileName",
                table: "UserLibrarySongs");

            migrationBuilder.DropColumn(
                name: "AudioSizeBytes",
                table: "UserLibrarySongs");

            migrationBuilder.DropColumn(
                name: "AudioStatus",
                table: "UserLibrarySongs");
        }
    }
}
