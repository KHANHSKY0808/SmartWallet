using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using CategoryService.Models;

namespace CategoryService.Data;

public static class DefaultCategorySeeder
{
    // 20 GUIDs cố định cho 20 danh mục mặc định (12 cha: 8 chi + 4 thu, 8 con)
    public static readonly Guid CatExpenseFood = Guid.Parse("10000000-0000-0000-0000-000000000001");
    public static readonly Guid CatExpenseFoodLunch = Guid.Parse("10000000-0000-0000-0000-000000000002");
    public static readonly Guid CatExpenseFoodCoffee = Guid.Parse("10000000-0000-0000-0000-000000000003");
    public static readonly Guid CatExpenseFoodSnack = Guid.Parse("10000000-0000-0000-0000-000000000004");

    public static readonly Guid CatExpenseTransport = Guid.Parse("10000000-0000-0000-0000-000000000005");
    public static readonly Guid CatExpenseTransportGas = Guid.Parse("10000000-0000-0000-0000-000000000006");
    public static readonly Guid CatExpenseTransportTaxi = Guid.Parse("10000000-0000-0000-0000-000000000007");

    public static readonly Guid CatExpenseHousing = Guid.Parse("10000000-0000-0000-0000-000000000008");
    public static readonly Guid CatExpenseHousingRent = Guid.Parse("10000000-0000-0000-0000-000000000009");
    public static readonly Guid CatExpenseHousingUtility = Guid.Parse("10000000-0000-0000-0000-000000000010");
    public static readonly Guid CatExpenseHousingInternet = Guid.Parse("10000000-0000-0000-0000-000000000011");

    public static readonly Guid CatExpenseHealth = Guid.Parse("10000000-0000-0000-0000-000000000012");
    public static readonly Guid CatExpenseShopping = Guid.Parse("10000000-0000-0000-0000-000000000013");
    public static readonly Guid CatExpenseEntertainment = Guid.Parse("10000000-0000-0000-0000-000000000014");
    public static readonly Guid CatExpenseEducation = Guid.Parse("10000000-0000-0000-0000-000000000015");
    public static readonly Guid CatExpenseOther = Guid.Parse("10000000-0000-0000-0000-000000000016");

    public static readonly Guid CatIncomeSalary = Guid.Parse("10000000-0000-0000-0000-000000000017");
    public static readonly Guid CatIncomeBonus = Guid.Parse("10000000-0000-0000-0000-000000000018");
    public static readonly Guid CatIncomeInvestment = Guid.Parse("10000000-0000-0000-0000-000000000019");
    public static readonly Guid CatIncomeOther = Guid.Parse("10000000-0000-0000-0000-000000000020");

    // FIN-97b & D44: 4 danh mục hệ thống cho sổ nợ (DB-09)
    public static readonly Guid CatExpenseLend = Guid.Parse("10000000-0000-0000-0000-000000000021");
    public static readonly Guid CatIncomeDebtCollection = Guid.Parse("10000000-0000-0000-0000-000000000022");
    public static readonly Guid CatIncomeBorrow = Guid.Parse("10000000-0000-0000-0000-000000000023");
    public static readonly Guid CatExpenseDebtRepayment = Guid.Parse("10000000-0000-0000-0000-000000000024");

    public static async Task SeedAsync(CategoryDbContext context, ILogger? logger = null)
    {
        // 16 danh mục cha (8 chi thường + 4 thu thường + 2 chi nợ + 2 thu nợ)
        var defaultParents = new List<Category>
        {
            // Chi tiêu (8 cha)
            new() { Id = CatExpenseFood, Name = "Ăn uống", Type = CategoryType.Expense, Icon = "utensils", Color = "#f97316", IsDefault = true, SortOrder = 1 },
            new() { Id = CatExpenseTransport, Name = "Di chuyển", Type = CategoryType.Expense, Icon = "car", Color = "#3b82f6", IsDefault = true, SortOrder = 2 },
            new() { Id = CatExpenseHousing, Name = "Nhà cửa", Type = CategoryType.Expense, Icon = "home", Color = "#10b981", IsDefault = true, SortOrder = 3 },
            new() { Id = CatExpenseHealth, Name = "Sức khỏe", Type = CategoryType.Expense, Icon = "heart", Color = "#ef4444", IsDefault = true, SortOrder = 4 },
            new() { Id = CatExpenseShopping, Name = "Mua sắm", Type = CategoryType.Expense, Icon = "shopping-bag", Color = "#ec4899", IsDefault = true, SortOrder = 5 },
            new() { Id = CatExpenseEntertainment, Name = "Giải trí", Type = CategoryType.Expense, Icon = "gamepad", Color = "#8b5cf6", IsDefault = true, SortOrder = 6 },
            new() { Id = CatExpenseEducation, Name = "Học tập", Type = CategoryType.Expense, Icon = "book", Color = "#06b6d4", IsDefault = true, SortOrder = 7 },
            new() { Id = CatExpenseOther, Name = "Khác", Type = CategoryType.Expense, Icon = "tag", Color = "#6b7280", IsDefault = true, SortOrder = 8 },

            // Thu nhập (4 cha)
            new() { Id = CatIncomeSalary, Name = "Lương", Type = CategoryType.Income, Icon = "money-bill", Color = "#22c55e", IsDefault = true, SortOrder = 9 },
            new() { Id = CatIncomeBonus, Name = "Thưởng", Type = CategoryType.Income, Icon = "gift", Color = "#eab308", IsDefault = true, SortOrder = 10 },
            new() { Id = CatIncomeInvestment, Name = "Đầu tư", Type = CategoryType.Income, Icon = "chart-line", Color = "#14b8a6", IsDefault = true, SortOrder = 11 },
            new() { Id = CatIncomeOther, Name = "Thu nhập khác", Type = CategoryType.Income, Icon = "coins", Color = "#64748b", IsDefault = true, SortOrder = 12 },

            // Sổ nợ (4 cha hệ thống - D44 / FIN-97b / DB-09)
            new() { Id = CatExpenseLend, Name = "Cho vay", Type = CategoryType.Expense, Icon = "hand-holding-usd", Color = "#ef4444", IsDefault = true, SortOrder = 13 },
            new() { Id = CatIncomeDebtCollection, Name = "Thu nợ", Type = CategoryType.Income, Icon = "hand-holding-usd", Color = "#10b981", IsDefault = true, SortOrder = 14 },
            new() { Id = CatIncomeBorrow, Name = "Đi vay", Type = CategoryType.Income, Icon = "receipt", Color = "#f59e0b", IsDefault = true, SortOrder = 15 },
            new() { Id = CatExpenseDebtRepayment, Name = "Trả nợ", Type = CategoryType.Expense, Icon = "money-bill-wave", Color = "#6366f1", IsDefault = true, SortOrder = 16 }
        };

        // 8 danh mục con
        var defaultChildren = new List<Category>
        {
            // Con của Ăn uống
            new() { Id = CatExpenseFoodLunch, ParentId = CatExpenseFood, Name = "Cơm trưa", Type = CategoryType.Expense, Icon = "bowl-rice", Color = "#f97316", IsDefault = true, SortOrder = 1 },
            new() { Id = CatExpenseFoodCoffee, ParentId = CatExpenseFood, Name = "Cà phê", Type = CategoryType.Expense, Icon = "mug-hot", Color = "#78350f", IsDefault = true, SortOrder = 2 },
            new() { Id = CatExpenseFoodSnack, ParentId = CatExpenseFood, Name = "Ăn vặt", Type = CategoryType.Expense, Icon = "cookie", Color = "#ea580c", IsDefault = true, SortOrder = 3 },

            // Con của Di chuyển
            new() { Id = CatExpenseTransportGas, ParentId = CatExpenseTransport, Name = "Xăng xe", Type = CategoryType.Expense, Icon = "gas-pump", Color = "#2563eb", IsDefault = true, SortOrder = 1 },
            new() { Id = CatExpenseTransportTaxi, ParentId = CatExpenseTransport, Name = "Grab/Taxi", Type = CategoryType.Expense, Icon = "taxi", Color = "#0284c7", IsDefault = true, SortOrder = 2 },

            // Con của Nhà cửa
            new() { Id = CatExpenseHousingRent, ParentId = CatExpenseHousing, Name = "Tiền nhà", Type = CategoryType.Expense, Icon = "building", Color = "#059669", IsDefault = true, SortOrder = 1 },
            new() { Id = CatExpenseHousingUtility, ParentId = CatExpenseHousing, Name = "Điện nước", Type = CategoryType.Expense, Icon = "bolt", Color = "#0d9488", IsDefault = true, SortOrder = 2 },
            new() { Id = CatExpenseHousingInternet, ParentId = CatExpenseHousing, Name = "Internet", Type = CategoryType.Expense, Icon = "wifi", Color = "#0891b2", IsDefault = true, SortOrder = 3 }
        };

        var allDefaults = defaultParents.Concat(defaultChildren).ToList();

        foreach (var category in allDefaults)
        {
            var existing = await context.Categories.FindAsync(category.Id);
            if (existing == null)
            {
                // D-01: Insert nếu chưa có
                context.Categories.Add(category);
            }
            // D-03: Nếu đã tồn tại, KHÔNG ghi đè chỉnh sửa của Admin
        }

        await context.SaveChangesAsync();
        logger?.LogInformation("Hoàn tất khởi tạo seed 24 danh mục mặc định.");
    }
}

