using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BudgetService.Migrations
{
    /// <inheritdoc />
    public partial class AddCategoryNameToBudget : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CategoryName",
                table: "Budgets",
                type: "TEXT",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CategoryName",
                table: "Budgets");
        }
    }
}
