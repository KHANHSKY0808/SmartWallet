using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransactionService.Migrations
{
    /// <inheritdoc />
    public partial class AddSourceTypeAndOccurrenceDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "OccurrenceDate",
                table: "Transactions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SourceId",
                table: "Transactions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SourceType",
                table: "Transactions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_SourceType_SourceId_OccurrenceDate",
                table: "Transactions",
                columns: new[] { "SourceType", "SourceId", "OccurrenceDate" },
                unique: true,
                filter: "SourceType = 2 AND SourceId IS NOT NULL AND OccurrenceDate IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Transactions_SourceType_SourceId_OccurrenceDate",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "OccurrenceDate",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "SourceId",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "SourceType",
                table: "Transactions");
        }
    }
}
