using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransactionService.Migrations
{
    /// <inheritdoc />
    public partial class AddImportJobsAndSourceTypeImport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Transactions_SourceType_SourceId_SourceSubId",
                table: "Transactions");

            migrationBuilder.CreateTable(
                name: "ImportJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    TempFilePath = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Extension = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    ColumnMappingJson = table.Column<string>(type: "TEXT", nullable: true),
                    TotalRows = table.Column<int>(type: "INTEGER", nullable: false),
                    Succeeded = table.Column<int>(type: "INTEGER", nullable: false),
                    Failed = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    ErrorMessage = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportJobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ImportJobErrors",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ImportJobId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RowNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    DateRaw = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    AmountRaw = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    TypeRaw = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    CategoryRaw = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    WalletRaw = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    NoteRaw = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportJobErrors", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ImportJobErrors_ImportJobs_ImportJobId",
                        column: x => x.ImportJobId,
                        principalTable: "ImportJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "UX_Transactions_Import_JobRow",
                table: "Transactions",
                columns: new[] { "SourceType", "SourceId", "SourceSubId" },
                unique: true,
                filter: "SourceType = 5 AND SourceId IS NOT NULL AND SourceSubId IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ImportJobErrors_ImportJobId_RowNumber",
                table: "ImportJobErrors",
                columns: new[] { "ImportJobId", "RowNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ImportJobs_UserId_CreatedAt",
                table: "ImportJobs",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "UX_ImportJobs_User_Active",
                table: "ImportJobs",
                column: "UserId",
                unique: true,
                filter: "Status IN (0, 1)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ImportJobErrors");

            migrationBuilder.DropTable(
                name: "ImportJobs");

            migrationBuilder.DropIndex(
                name: "UX_Transactions_Import_JobRow",
                table: "Transactions");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_SourceType_SourceId_SourceSubId",
                table: "Transactions",
                columns: new[] { "SourceType", "SourceId", "SourceSubId" },
                unique: true,
                filter: "SourceType = 3 AND SourceSubId IS NOT NULL");
        }
    }
}
