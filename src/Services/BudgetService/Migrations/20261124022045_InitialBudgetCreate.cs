using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BudgetService.Migrations
{
    /// <inheritdoc />
    public partial class InitialBudgetCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Budgets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CategoryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Month = table.Column<int>(type: "INTEGER", nullable: false),
                    Year = table.Column<int>(type: "INTEGER", nullable: false),
                    CurrentSpent = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    IsAlertSent80 = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsAlertSent100 = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Budgets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BudgetTransactions",
                columns: table => new
                {
                    BudgetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TransactionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TransactionDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BudgetTransactions", x => new { x.BudgetId, x.TransactionId });
                    table.ForeignKey(
                        name: "FK_BudgetTransactions_Budgets_BudgetId",
                        column: x => x.BudgetId,
                        principalTable: "Budgets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Budgets_User_Category_Month_Year",
                table: "Budgets",
                columns: new[] { "UserId", "CategoryId", "Month", "Year" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Budgets_User_Month_Year",
                table: "Budgets",
                columns: new[] { "UserId", "Month", "Year" });

            migrationBuilder.CreateIndex(
                name: "IX_BudgetTransactions_TransactionId",
                table: "BudgetTransactions",
                column: "TransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_BudgetTransactions_User_Date",
                table: "BudgetTransactions",
                columns: new[] { "UserId", "TransactionDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BudgetTransactions");

            migrationBuilder.DropTable(
                name: "Budgets");
        }
    }
}
