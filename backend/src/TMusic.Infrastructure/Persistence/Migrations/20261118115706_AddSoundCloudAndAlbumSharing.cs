using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TMusic.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSoundCloudAndAlbumSharing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AlbumShares",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AlbumId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ShareCode = table.Column<string>(type: "varchar(24)", unicode: false, maxLength: 24, nullable: false, collation: "Latin1_General_100_BIN2"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AlbumShares", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AlbumShares_Albums_AlbumId_OwnerId",
                        columns: x => new { x.AlbumId, x.OwnerId },
                        principalTable: "Albums",
                        principalColumns: new[] { "Id", "OwnerId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AlbumShares_AlbumId_OwnerId",
                table: "AlbumShares",
                columns: new[] { "AlbumId", "OwnerId" });

            migrationBuilder.CreateIndex(
                name: "IX_AlbumShares_AlbumId_RevokedAt",
                table: "AlbumShares",
                columns: new[] { "AlbumId", "RevokedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AlbumShares_ShareCode",
                table: "AlbumShares",
                column: "ShareCode",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AlbumShares");
        }
    }
}
