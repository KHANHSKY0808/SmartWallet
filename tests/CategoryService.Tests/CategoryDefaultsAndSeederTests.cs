using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using CategoryService.Controllers;
using CategoryService.Data;
using CategoryService.DTOs;
using CategoryService.Models;

namespace CategoryService.Tests;

public class CategoryDefaultsAndSeederTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly CategoryDbContext _context;
    private readonly CategoryDefaultsController _adminController;
    private readonly CategoriesController _userController;
    private readonly Guid _userA = Guid.NewGuid();

    public CategoryDefaultsAndSeederTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<CategoryDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new CategoryDbContext(options);
        _context.Database.EnsureCreated();

        _adminController = new CategoryDefaultsController(_context, NullLogger<CategoryDefaultsController>.Instance);
        _userController = new CategoriesController(_context, NullLogger<CategoriesController>.Instance);

        SetAdminContext();
    }

    private void SetAdminContext()
    {
        var claims = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Role, "Admin")
        }, "TestAuth"));

        _adminController.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = claims }
        };
    }

    private void SetUserContext()
    {
        var claims = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, _userA.ToString()),
            new Claim("sub", _userA.ToString()),
            new Claim(ClaimTypes.Role, "User")
        }, "TestAuth"));

        _userController.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = claims }
        };
    }

    [Fact]
    public async Task D01_Seeder_EmptyDb_Creates20DefaultCategoriesWithNullUser()
    {
        await DefaultCategorySeeder.SeedAsync(_context);

        var all = await _context.Categories.ToListAsync();
        Assert.Equal(20, all.Count);
        Assert.All(all, c =>
        {
            Assert.Null(c.UserId);
            Assert.True(c.IsDefault);
        });

        // 12 cha, 8 con
        Assert.Equal(12, all.Count(c => c.ParentId == null));
        Assert.Equal(8, all.Count(c => c.ParentId != null));
    }

    [Fact]
    public async Task D02_Seeder_RunTwice_IsIdempotentAndDoesNotDuplicate()
    {
        await DefaultCategorySeeder.SeedAsync(_context);
        await DefaultCategorySeeder.SeedAsync(_context);

        var count = await _context.Categories.CountAsync();
        Assert.Equal(20, count);
    }

    [Fact]
    public async Task D03_Seeder_AdminRenamedCategory_DoesNotOverwriteAdminEdits()
    {
        await DefaultCategorySeeder.SeedAsync(_context);

        var otherCat = await _context.Categories.FindAsync(DefaultCategorySeeder.CatExpenseOther);
        Assert.NotNull(otherCat);
        otherCat.Name = "Chi khác";
        await _context.SaveChangesAsync();

        // Chạy lại seeder
        await DefaultCategorySeeder.SeedAsync(_context);

        var recheck = await _context.Categories.FindAsync(DefaultCategorySeeder.CatExpenseOther);
        Assert.NotNull(recheck);
        Assert.Equal("Chi khác", recheck.Name);
    }

    [Fact]
    public async Task D04_NonAdmin_AccessDefaultsEndpoint_ForbiddenInIntegration()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        // Không gửi token Admin gọi defaults -> 401 hoặc 403
        var res = await client.PostAsJsonAsync("/api/categories/defaults", new CreateCategoryRequest { Name = "Test", Type = 1 });
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task D05_Admin_CreateDefaultCategory_AppearsImmediatelyForRegularUser()
    {
        await DefaultCategorySeeder.SeedAsync(_context);

        var req = new CreateCategoryRequest { Name = "Từ thiện", Type = 1 };
        var adminRes = await _adminController.CreateDefaultCategory(req) as ObjectResult;

        Assert.NotNull(adminRes);
        Assert.Equal(201, adminRes.StatusCode);
        var created = (CategoryDto)adminRes.Value!;
        Assert.Null(created.UserId);
        Assert.True(created.IsDefault);

        // User A gọi GET thấy ngay
        SetUserContext();
        var userRes = await _userController.GetCategories() as OkObjectResult;
        var list = (List<CategoryDto>)userRes!.Value!;
        Assert.Contains(list, c => c.Name == "Từ thiện" && c.IsDefault);
    }

    [Fact]
    public async Task D06_Admin_DeleteDefaultCategory_SoftDeletesAndHidesFromUser()
    {
        await DefaultCategorySeeder.SeedAsync(_context);

        var delRes = await _adminController.DeleteDefaultCategory(DefaultCategorySeeder.CatExpenseEducation) as OkObjectResult;
        Assert.NotNull(delRes);

        var inDb = await _context.Categories.FindAsync(DefaultCategorySeeder.CatExpenseEducation);
        Assert.NotNull(inDb);
        Assert.False(inDb.IsActive);

        SetUserContext();
        var userRes = await _userController.GetCategories() as OkObjectResult;
        var list = (List<CategoryDto>)userRes!.Value!;
        Assert.DoesNotContain(list, c => c.Id == DefaultCategorySeeder.CatExpenseEducation);
    }

    [Fact]
    public void D07_NoUserRegisteredConsumerRegistered_InServices()
    {
        // Kiểm tra Program / DI của CategoryService: Không được đăng ký bất kỳ Consumer nào cho UserRegistered (D13)
        var services = new ServiceCollection();
        // Giả lập hoặc quét assembly
        var consumerTypes = typeof(CategoryDbContext).Assembly.GetTypes()
            .Where(t => t.Name.Contains("UserRegistered", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Empty(consumerTypes);
    }

    [Fact]
    public async Task D08_Admin_UpdateDefaultCategory_Valid_UpdatesNameAndColor()
    {
        await DefaultCategorySeeder.SeedAsync(_context);

        var updateReq = new UpdateCategoryRequest { Name = "Khác Đổi Tên", Color = "#123456" };
        var res = await _adminController.UpdateDefaultCategory(DefaultCategorySeeder.CatExpenseOther, updateReq) as OkObjectResult;

        Assert.NotNull(res);
        var updated = (CategoryDto)res.Value!;
        Assert.Equal("Khác Đổi Tên", updated.Name);
        Assert.Equal("#123456", updated.Color);
    }

    [Fact]
    public async Task D09_Admin_UpdateDefaultCategory_NotFound_Returns404()
    {
        var updateReq = new UpdateCategoryRequest { Name = "Không tồn tại" };
        var res = await _adminController.UpdateDefaultCategory(Guid.NewGuid(), updateReq) as NotFoundObjectResult;

        Assert.NotNull(res);
        Assert.Equal(404, res.StatusCode);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}
