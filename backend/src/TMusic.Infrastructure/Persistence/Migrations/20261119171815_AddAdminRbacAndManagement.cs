using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TMusic.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAdminRbacAndManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsAdminSession",
                table: "UserSessions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastSeenAt",
                table: "UserSessions",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "MfaVerified",
                table: "UserSessions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ModeratedAt",
                table: "Songs",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ModerationReason",
                table: "Songs",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ModerationStatus",
                table: "Songs",
                type: "varchar(24)",
                unicode: false,
                maxLength: 24,
                nullable: false,
                defaultValue: "ACTIVE");

            migrationBuilder.AddColumn<bool>(
                name: "IsSuspended",
                table: "AspNetUsers",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastActiveAt",
                table: "AspNetUsers",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SuspendedUntil",
                table: "AspNetUsers",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SuspensionReason",
                table: "AspNetUsers",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AccountSuspensions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Action = table.Column<string>(type: "varchar(24)", unicode: false, maxLength: 24, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CorrelationId = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountSuspensions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccountSuspensions_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AdminInvitations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    NormalizedEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    RoleName = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    TokenHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    InvitedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AcceptedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    AcceptedUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RevokedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RevokedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminInvitations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AdminMfaChallenges",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Purpose = table.Column<string>(type: "varchar(24)", unicode: false, maxLength: 24, nullable: false),
                    DeviceName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ConsumedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminMfaChallenges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AdminMfaChallenges_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ActorEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ActorRole = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    Action = table.Column<string>(type: "varchar(80)", unicode: false, maxLength: 80, nullable: false),
                    TargetType = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    TargetId = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: true),
                    Result = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    MetadataJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    CorrelationId = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: true),
                    IpAddress = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ContentModerationFlags",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TargetType = table.Column<string>(type: "varchar(24)", unicode: false, maxLength: 24, nullable: false),
                    TargetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FlagType = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "varchar(24)", unicode: false, maxLength: 24, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ReportedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReviewedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ResolutionNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentModerationFlags", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SystemEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Severity = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    EventType = table.Column<string>(type: "varchar(48)", unicode: false, maxLength: 48, nullable: false),
                    Code = table.Column<string>(type: "varchar(80)", unicode: false, maxLength: 80, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    CorrelationId = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: true),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    MetadataJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SystemSettings",
                columns: table => new
                {
                    Key = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Value = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    DefaultValue = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ValueType = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    Category = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    MinValue = table.Column<int>(type: "int", nullable: true),
                    MaxValue = table.Column<int>(type: "int", nullable: true),
                    RequiresConfirmation = table.Column<bool>(type: "bit", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemSettings", x => x.Key);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Songs_ModerationStatus",
                table: "Songs",
                column: "ModerationStatus");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_CreatedAt",
                table: "AspNetUsers",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_IsSuspended",
                table: "AspNetUsers",
                column: "IsSuspended");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_LastActiveAt",
                table: "AspNetUsers",
                column: "LastActiveAt");

            migrationBuilder.CreateIndex(
                name: "IX_AccountSuspensions_UserId_CreatedAt",
                table: "AccountSuspensions",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AdminInvitations_NormalizedEmail_ExpiresAt",
                table: "AdminInvitations",
                columns: new[] { "NormalizedEmail", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AdminInvitations_TokenHash",
                table: "AdminInvitations",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AdminMfaChallenges_ExpiresAt",
                table: "AdminMfaChallenges",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_AdminMfaChallenges_UserId",
                table: "AdminMfaChallenges",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_Action_CreatedAt",
                table: "AuditLogs",
                columns: new[] { "Action", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_ActorId_CreatedAt",
                table: "AuditLogs",
                columns: new[] { "ActorId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_CreatedAt",
                table: "AuditLogs",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ContentModerationFlags_Status_CreatedAt",
                table: "ContentModerationFlags",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ContentModerationFlags_TargetType_TargetId",
                table: "ContentModerationFlags",
                columns: new[] { "TargetType", "TargetId" });

            migrationBuilder.CreateIndex(
                name: "IX_SystemEvents_CorrelationId",
                table: "SystemEvents",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_SystemEvents_CreatedAt",
                table: "SystemEvents",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_SystemEvents_EventType_CreatedAt",
                table: "SystemEvents",
                columns: new[] { "EventType", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SystemEvents_Severity_CreatedAt",
                table: "SystemEvents",
                columns: new[] { "Severity", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccountSuspensions");

            migrationBuilder.DropTable(
                name: "AdminInvitations");

            migrationBuilder.DropTable(
                name: "AdminMfaChallenges");

            migrationBuilder.DropTable(
                name: "AuditLogs");

            migrationBuilder.DropTable(
                name: "ContentModerationFlags");

            migrationBuilder.DropTable(
                name: "SystemEvents");

            migrationBuilder.DropTable(
                name: "SystemSettings");

            migrationBuilder.DropIndex(
                name: "IX_Songs_ModerationStatus",
                table: "Songs");

            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_CreatedAt",
                table: "AspNetUsers");

            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_IsSuspended",
                table: "AspNetUsers");

            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_LastActiveAt",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "IsAdminSession",
                table: "UserSessions");

            migrationBuilder.DropColumn(
                name: "LastSeenAt",
                table: "UserSessions");

            migrationBuilder.DropColumn(
                name: "MfaVerified",
                table: "UserSessions");

            migrationBuilder.DropColumn(
                name: "ModeratedAt",
                table: "Songs");

            migrationBuilder.DropColumn(
                name: "ModerationReason",
                table: "Songs");

            migrationBuilder.DropColumn(
                name: "ModerationStatus",
                table: "Songs");

            migrationBuilder.DropColumn(
                name: "IsSuspended",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "LastActiveAt",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "SuspendedUntil",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "SuspensionReason",
                table: "AspNetUsers");
        }
    }
}
