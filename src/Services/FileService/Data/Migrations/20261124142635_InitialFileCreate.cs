using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FileService.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialFileCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FileMetadata",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Purpose = table.Column<int>(type: "INTEGER", nullable: false),
                    OriginalFileName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    StoragePath = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    StorageProvider = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false, defaultValue: "Local"),
                    ContentType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    SizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    Sha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    IsEncrypted = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    RelatedEntityType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    RelatedEntityId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FileMetadata", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FileMetadata_OwnerUserId_Purpose_CreatedAt",
                table: "FileMetadata",
                columns: new[] { "OwnerUserId", "Purpose", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_FileMetadata_StorageProvider_StoragePath",
                table: "FileMetadata",
                columns: new[] { "StorageProvider", "StoragePath" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FileMetadata");
        }
    }
}
