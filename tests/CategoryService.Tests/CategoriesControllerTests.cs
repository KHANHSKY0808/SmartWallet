using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using CategoryService.Controllers;
using CategoryService.Data;
using CategoryService.DTOs;
using CategoryService.Models;

namespace CategoryService.Tests;

public class CategoriesControllerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly CategoryDbContext _context;
    private readonly CategoriesController _controller;
    private readonly Guid _userA = Guid.NewGuid();
    private readonly Guid _userB = Guid.NewGuid();

    public CategoriesControllerTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<CategoryDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new CategoryDbContext(options);
        _context.Database.EnsureCreated();

        // Seed 20 danh mục mặc định
        DefaultCategorySeeder.SeedAsync(_context).GetAwaiter().GetResult();

        _controller = new CategoriesController(_context, NullLogger<CategoriesController>.Instance);
        SetCurrentUser(_userA);
    }

    private void SetCurrentUser(Guid userId, string role = "User")
    {
        var claims = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim("sub", userId.ToString()),
            new Claim(ClaimTypes.Role, role)
        }, "TestAuth"));

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = claims }
        };
    }

    [Fact]
    public async Task C01_GetCategories_UserHasNoCustomCategories_Returns20DefaultCategoriesInTree()
    {
        var result = await _controller.GetCategories() as OkObjectResult;

        Assert.NotNull(result);
        var categories = Assert.IsAssignableFrom<List<CategoryDto>>(result.Value);

        // 12 danh mục cha
        Assert.Equal(12, categories.Count);

        // Tổng cộng cả cha và con = 20
        var totalCount = categories.Count + categories.Sum(c => c.Children.Count);
        Assert.Equal(20, totalCount);

        // Các con lồng đúng dưới cha Ăn uống
        var food = categories.FirstOrDefault(c => c.Id == DefaultCategorySeeder.CatExpenseFood);
        Assert.NotNull(food);
        Assert.Equal(3, food.Children.Count);
    }

    [Fact]
    public async Task C02_CreateCategory_PersonalCategory_Returns201WithUserAndNotDefault()
    {
        var request = new CreateCategoryRequest
        {
            Name = "Thú cưng",
            Type = 1
        };

        var result = await _controller.CreateCategory(request) as ObjectResult;

        Assert.NotNull(result);
        Assert.Equal(201, result.StatusCode);
        var dto = Assert.IsType<CategoryDto>(result.Value);
        Assert.Equal(_userA, dto.UserId);
        Assert.Equal("Thú cưng", dto.Name);
        Assert.False(dto.IsDefault);
        Assert.Null(dto.ParentId);
    }

    [Fact]
    public async Task C03_CreateCategory_ChildUnderPersonalCategory_Returns201AndAppearsUnderParent()
    {
        var parentRes = await _controller.CreateCategory(new CreateCategoryRequest { Name = "Thú cưng", Type = 1 }) as ObjectResult;
        var parentId = ((CategoryDto)parentRes!.Value!).Id;

        var childRes = await _controller.CreateCategory(new CreateCategoryRequest { Name = "Hạt mèo", Type = 1, ParentId = parentId }) as ObjectResult;
        Assert.NotNull(childRes);
        Assert.Equal(201, childRes.StatusCode);

        var treeRes = await _controller.GetCategories() as OkObjectResult;
        var tree = (List<CategoryDto>)treeRes!.Value!;
        var parentInTree = tree.FirstOrDefault(c => c.Id == parentId);
        Assert.NotNull(parentInTree);
        Assert.Single(parentInTree.Children);
        Assert.Equal("Hạt mèo", parentInTree.Children[0].Name);
    }

    [Fact]
    public async Task C04_CreateCategory_PersonalChildUnderDefaultParent_Returns201()
    {
        // Tạo "Trà sữa" dưới cha mặc định "Ăn uống"
        var request = new CreateCategoryRequest
        {
            Name = "Trà sữa",
            Type = 1,
            ParentId = DefaultCategorySeeder.CatExpenseFood
        };

        var result = await _controller.CreateCategory(request) as ObjectResult;
        Assert.NotNull(result);
        Assert.Equal(201, result.StatusCode);
        var dto = Assert.IsType<CategoryDto>(result.Value);
        Assert.Equal(_userA, dto.UserId);
        Assert.Equal(DefaultCategorySeeder.CatExpenseFood, dto.ParentId);
    }

    [Fact]
    public async Task C05_CreateCategory_Level3Child_Returns400()
    {
        // Tạo cấp 1 (Thú cưng) -> cấp 2 (Hạt mèo)
        var pRes = await _controller.CreateCategory(new CreateCategoryRequest { Name = "Thú cưng", Type = 1 }) as ObjectResult;
        var pId = ((CategoryDto)pRes!.Value!).Id;

        var cRes = await _controller.CreateCategory(new CreateCategoryRequest { Name = "Hạt mèo", Type = 1, ParentId = pId }) as ObjectResult;
        var cId = ((CategoryDto)cRes!.Value!).Id;

        // Thử tạo cấp 3 dưới "Hạt mèo"
        var level3Req = new CreateCategoryRequest { Name = "Hạt Royal Canin", Type = 1, ParentId = cId };
        var level3Res = await _controller.CreateCategory(level3Req) as BadRequestObjectResult;

        Assert.NotNull(level3Res);
        Assert.Equal(400, level3Res.StatusCode);
    }

    [Fact]
    public async Task C06_CreateCategory_ChildDifferentTypeFromParent_Returns400()
    {
        // Cha là Expense, con là Income
        var req = new CreateCategoryRequest
        {
            Name = "Tiền phụ cấp ăn uống",
            Type = 2, // Income
            ParentId = DefaultCategorySeeder.CatExpenseFood // Expense
        };

        var res = await _controller.CreateCategory(req) as BadRequestObjectResult;
        Assert.NotNull(res);
        Assert.Equal(400, res.StatusCode);
    }

    [Fact]
    public async Task C07_UpdateCategory_SelfAsParent_Returns400()
    {
        var res = await _controller.CreateCategory(new CreateCategoryRequest { Name = "Tự do", Type = 1 }) as ObjectResult;
        var id = ((CategoryDto)res!.Value!).Id;

        var updateReq = new UpdateCategoryRequest { Name = "Tự do", ParentId = id };
        var updateRes = await _controller.UpdateCategory(id, updateReq) as BadRequestObjectResult;

        Assert.NotNull(updateRes);
        Assert.Equal(400, updateRes.StatusCode);
    }

    [Fact]
    public async Task C08_AccessOtherUserCategory_Returns404AndNotVisibleInGet()
    {
        SetCurrentUser(_userB);
        var resB = await _controller.CreateCategory(new CreateCategoryRequest { Name = "Bí mật của B", Type = 1 }) as ObjectResult;
        var idB = ((CategoryDto)resB!.Value!).Id;

        SetCurrentUser(_userA);
        var getRes = await _controller.GetCategory(idB) as NotFoundObjectResult;
        var putRes = await _controller.UpdateCategory(idB, new UpdateCategoryRequest { Name = "Hack" }) as NotFoundObjectResult;
        var delRes = await _controller.DeleteCategory(idB) as NotFoundObjectResult;

        Assert.NotNull(getRes); Assert.Equal(404, getRes.StatusCode);
        Assert.NotNull(putRes); Assert.Equal(404, putRes.StatusCode);
        Assert.NotNull(delRes); Assert.Equal(404, delRes.StatusCode);

        var listRes = await _controller.GetCategories() as OkObjectResult;
        var list = (List<CategoryDto>)listRes!.Value!;
        Assert.DoesNotContain(list, c => c.Id == idB);
    }

    [Fact]
    public async Task C09_RegularUser_PutOrDeleteDefaultCategory_Returns403()
    {
        var putRes = await _controller.UpdateCategory(DefaultCategorySeeder.CatExpenseFood, new UpdateCategoryRequest { Name = "Ăn uống mới" }) as ObjectResult;
        var delRes = await _controller.DeleteCategory(DefaultCategorySeeder.CatExpenseFood) as ObjectResult;

        Assert.NotNull(putRes); Assert.Equal(403, putRes.StatusCode);
        Assert.NotNull(delRes); Assert.Equal(403, delRes.StatusCode);
    }

    [Fact]
    public async Task C10_CreateCategory_DuplicateNameSameParent_Returns409()
    {
        await _controller.CreateCategory(new CreateCategoryRequest { Name = "Thú cưng", Type = 1 });

        var dupRes = await _controller.CreateCategory(new CreateCategoryRequest { Name = "thú CƯNG", Type = 1 }) as ObjectResult;
        Assert.NotNull(dupRes);
        Assert.Equal(409, dupRes.StatusCode);
    }

    [Fact]
    public async Task C11_CreateCategory_PersonalRootSameNameAsDefaultRoot_Returns409()
    {
        // "Ăn uống" là danh mục mặc định cấp gốc loại Expense
        var req = new CreateCategoryRequest { Name = "Ăn uống", Type = 1, ParentId = null };
        var res = await _controller.CreateCategory(req) as ObjectResult;

        Assert.NotNull(res);
        Assert.Equal(409, res.StatusCode);
    }

    [Fact]
    public async Task C12_DeleteCategory_ParentHasActiveChildren_Returns409HasChildren()
    {
        var pRes = await _controller.CreateCategory(new CreateCategoryRequest { Name = "Thú cưng", Type = 1 }) as ObjectResult;
        var pId = ((CategoryDto)pRes!.Value!).Id;
        await _controller.CreateCategory(new CreateCategoryRequest { Name = "Hạt mèo", Type = 1, ParentId = pId });

        var delRes = await _controller.DeleteCategory(pId) as ObjectResult;
        Assert.NotNull(delRes);
        Assert.Equal(409, delRes.StatusCode);
    }

    [Fact]
    public async Task C13_DeleteCategory_LeafWithReplacementId_SoftDeletesAndHidesFromGet()
    {
        var pRes = await _controller.CreateCategory(new CreateCategoryRequest { Name = "Thú cưng", Type = 1 }) as ObjectResult;
        var pId = ((CategoryDto)pRes!.Value!).Id;
        var cRes = await _controller.CreateCategory(new CreateCategoryRequest { Name = "Hạt mèo", Type = 1, ParentId = pId }) as ObjectResult;
        var cId = ((CategoryDto)cRes!.Value!).Id;

        var delRes = await _controller.DeleteCategory(cId, replacementId: pId) as OkObjectResult;
        Assert.NotNull(delRes);

        var inDb = await _context.Categories.FindAsync(cId);
        Assert.NotNull(inDb);
        Assert.False(inDb.IsActive);

        var listRes = await _controller.GetCategories() as OkObjectResult;
        var list = (List<CategoryDto>)listRes!.Value!;
        var pInTree = list.FirstOrDefault(c => c.Id == pId);
        Assert.NotNull(pInTree);
        Assert.Empty(pInTree.Children);
    }

    [Fact]
    public async Task C14_DeleteCategory_InvalidReplacementId_Returns400AndCategoryRemainsActive()
    {
        var res = await _controller.CreateCategory(new CreateCategoryRequest { Name = "Hạt mèo", Type = 1 }) as ObjectResult;
        var id = ((CategoryDto)res!.Value!).Id;

        // ReplacementId trùng chính nó
        var selfRes = await _controller.DeleteCategory(id, replacementId: id) as BadRequestObjectResult;
        Assert.NotNull(selfRes);
        Assert.Equal(400, selfRes.StatusCode);

        // ReplacementId không tồn tại
        var nonExistRes = await _controller.DeleteCategory(id, replacementId: Guid.NewGuid()) as BadRequestObjectResult;
        Assert.NotNull(nonExistRes);
        Assert.Equal(400, nonExistRes.StatusCode);

        // ReplacementId khác loại thu/chi (Lương là Income, Hạt mèo là Expense)
        var diffTypeRes = await _controller.DeleteCategory(id, replacementId: DefaultCategorySeeder.CatIncomeSalary) as BadRequestObjectResult;
        Assert.NotNull(diffTypeRes);
        Assert.Equal(400, diffTypeRes.StatusCode);

        var inDb = await _context.Categories.FindAsync(id);
        Assert.NotNull(inDb);
        Assert.True(inDb.IsActive);
    }

    [Theory]
    [InlineData("invalid-color")]
    [InlineData("#12345")]
    [InlineData("#1234567")]
    public async Task C15_CreateCategory_InvalidColor_Returns400(string invalidColor)
    {
        var req = new CreateCategoryRequest { Name = "Ví dụ", Type = 1, Color = invalidColor };
        var res = await _controller.CreateCategory(req) as BadRequestObjectResult;
        Assert.NotNull(res);
        Assert.Equal(400, res.StatusCode);
    }

    [Fact]
    public async Task C16_GetCategories_FilteredByType_ReturnsOnlyMatchingType()
    {
        var res = await _controller.GetCategories(type: 2) as OkObjectResult;
        Assert.NotNull(res);
        var categories = (List<CategoryDto>)res.Value!;

        Assert.All(categories, c => Assert.Equal(CategoryType.Income, c.Type));
        Assert.Equal(4, categories.Count);
    }

    [Fact]
    public async Task C17_Migration_RunsOnEmptyDb_CreatesCategoriesTable()
    {
        await using var migConn = new SqliteConnection("Filename=:memory:");
        await migConn.OpenAsync();

        var options = new DbContextOptionsBuilder<CategoryDbContext>()
            .UseSqlite(migConn)
            .Options;

        await using var migContext = new CategoryDbContext(options);
        await migContext.Database.MigrateAsync();

        await using var cmd = migConn.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name = 'Categories';";
        var tableName = await cmd.ExecuteScalarAsync();
        Assert.Equal("Categories", tableName);
    }

    [Fact]
    public async Task C18_UpdateCategory_Valid_UpdatesNameAndIcon()
    {
        var res = await _controller.CreateCategory(new CreateCategoryRequest { Name = "Tên Cũ", Type = 1 }) as ObjectResult;
        var id = ((CategoryDto)res!.Value!).Id;

        var updateRes = await _controller.UpdateCategory(id, new UpdateCategoryRequest { Name = "Tên Mới", Icon = "star", Color = "#abcdef" }) as OkObjectResult;
        Assert.NotNull(updateRes);
        var updated = (CategoryDto)updateRes.Value!;
        Assert.Equal("Tên Mới", updated.Name);
        Assert.Equal("star", updated.Icon);
        Assert.Equal("#abcdef", updated.Color);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}
