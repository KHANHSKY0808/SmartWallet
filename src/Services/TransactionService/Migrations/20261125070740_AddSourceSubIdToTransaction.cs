using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransactionService.Migrations
{
    /// <inheritdoc />
    public partial class AddSourceSubIdToTransaction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SourceSubId",
                table: "Transactions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_SourceType_SourceId_SourceSubId",
                table: "Transactions",
                columns: new[] { "SourceType", "SourceId", "SourceSubId" },
                unique: true,
                filter: "SourceType = 3 AND SourceSubId IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Transactions_SourceType_SourceId_SourceSubId",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "SourceSubId",
                table: "Transactions");
        }
    }
}
