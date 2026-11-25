using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using WalletService.Controllers;
using WalletService.Data;
using WalletService.DTOs;
using WalletService.Models;
using WalletService.Services;

namespace WalletService.Tests;

public class WalletsControllerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly WalletDbContext _context;
    private readonly WalletLedger _ledger;
    private readonly TestUserProfileClient _profileClient;
    private readonly TestEventPublisher _publisher;
    private readonly TestSavingGoalClient _savingGoalClient;
    private readonly WalletsController _controller;
    private readonly Guid _userA = Guid.NewGuid();
    private readonly Guid _userB = Guid.NewGuid();

    public WalletsControllerTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<WalletDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new WalletDbContext(options);
        _context.Database.EnsureCreated();

        _ledger = new WalletLedger(_context);
        _profileClient = new TestUserProfileClient();
        _publisher = new TestEventPublisher();
        _savingGoalClient = new TestSavingGoalClient();

        _controller = new WalletsController(
            _context,
            _ledger,
            _profileClient,
            NullLogger<WalletsController>.Instance,
            _publisher,
            _savingGoalClient
        );

        SetCurrentUser(_userA);
    }

    private void SetCurrentUser(Guid userId)
    {
        var claims = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim("sub", userId.ToString())
        }, "TestAuth"));

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = claims }
        };
    }

    private WalletDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<WalletDbContext>()
            .UseSqlite(_connection)
            .Options;
        return new WalletDbContext(options);
    }

    [Fact]
    public async Task W01_CreateWallet_ValidRequest_Returns201AndSetsInitialBalanceAndUserFromClaim()
    {
        var request = new CreateWalletRequest
        {
            Name = "Tiền mặt",
            Type = 1,
            InitialBalance = 5_000_000m,
            Currency = "VND"
        };

        var result = await _controller.CreateWallet(request) as ObjectResult;

        Assert.NotNull(result);
        Assert.Equal(201, result.StatusCode);
        var dto = Assert.IsType<WalletDto>(result.Value);
        Assert.Equal(_userA, dto.UserId);
        Assert.Equal("Tiền mặt", dto.Name);
        Assert.Equal(5_000_000m, dto.Balance);
        Assert.Equal(5_000_000m, dto.InitialBalance);
        Assert.False(dto.IsArchived);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(-1)]
    public async Task W02_CreateWallet_InvalidType_Returns400(int invalidType)
    {
        var request = new CreateWalletRequest
        {
            Name = "Ví test",
            Type = invalidType,
            InitialBalance = 1_000_000m
        };

        var result = await _controller.CreateWallet(request) as BadRequestObjectResult;
        Assert.NotNull(result);
        Assert.Equal(400, result.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task W03_CreateWallet_EmptyOrWhitespaceName_Returns400(string invalidName)
    {
        var request = new CreateWalletRequest
        {
            Name = invalidName,
            Type = 1,
            InitialBalance = 1_000_000m
        };

        var result = await _controller.CreateWallet(request) as BadRequestObjectResult;
        Assert.NotNull(result);
        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task W03_CreateWallet_NameExceeds100Chars_Returns400()
    {
        var request = new CreateWalletRequest
        {
            Name = new string('A', 101),
            Type = 1,
            InitialBalance = 1_000_000m
        };

        var result = await _controller.CreateWallet(request) as BadRequestObjectResult;
        Assert.NotNull(result);
        Assert.Equal(400, result.StatusCode);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task W04_CreateWallet_InitialBalanceNegative_Returns400(int type)
    {
        var request = new CreateWalletRequest
        {
            Name = "Ví âm",
            Type = type,
            InitialBalance = -100_000m
        };

        var result = await _controller.CreateWallet(request) as BadRequestObjectResult;
        Assert.NotNull(result);
        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task W05_CreateWallet_NoCurrency_DefaultsToVND()
    {
        var request = new CreateWalletRequest
        {
            Name = "Ví mặc định VND",
            Type = 1,
            InitialBalance = 500_000m,
            Currency = null
        };

        var result = await _controller.CreateWallet(request) as ObjectResult;
        Assert.NotNull(result);
        Assert.Equal(201, result.StatusCode);
        var dto = Assert.IsType<WalletDto>(result.Value);
        Assert.Equal("VND", dto.Currency);
    }

    [Fact]
    public async Task W06_CreateWallet_UnsupportedCurrency_Returns400()
    {
        var request = new CreateWalletRequest
        {
            Name = "Ví XYZ",
            Type = 1,
            InitialBalance = 500_000m,
            Currency = "XYZ"
        };

        var result = await _controller.CreateWallet(request) as BadRequestObjectResult;
        Assert.NotNull(result);
        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task W07_CreateWallet_DuplicateNameSameUser_Returns409()
    {
        await _controller.CreateWallet(new CreateWalletRequest { Name = "Tiền mặt", Type = 1, InitialBalance = 1_000_000m });

        var duplicateResult = await _controller.CreateWallet(new CreateWalletRequest { Name = "  tiền MẶT ", Type = 1, InitialBalance = 2_000_000m }) as ObjectResult;

        Assert.NotNull(duplicateResult);
        Assert.Equal(409, duplicateResult.StatusCode);
    }

    [Fact]
    public async Task W08_CreateWallet_ReusingNameOfArchivedWallet_Returns201()
    {
        var createRes = await _controller.CreateWallet(new CreateWalletRequest { Name = "Tiền mặt", Type = 1, InitialBalance = 1_000_000m }) as ObjectResult;
        var dto = (WalletDto)createRes!.Value!;
        await _controller.DeleteWallet(dto.Id);

        var newRes = await _controller.CreateWallet(new CreateWalletRequest { Name = "Tiền mặt", Type = 1, InitialBalance = 500_000m }) as ObjectResult;
        Assert.NotNull(newRes);
        Assert.Equal(201, newRes.StatusCode);
    }

    [Fact]
    public async Task W09_CreateWallet_DifferentUserSameName_Returns201()
    {
        await _controller.CreateWallet(new CreateWalletRequest { Name = "Tiền mặt", Type = 1, InitialBalance = 1_000_000m });

        SetCurrentUser(_userB);
        var userBRes = await _controller.CreateWallet(new CreateWalletRequest { Name = "Tiền mặt", Type = 1, InitialBalance = 500_000m }) as ObjectResult;

        Assert.NotNull(userBRes);
        Assert.Equal(201, userBRes.StatusCode);
    }

    [Fact]
    public async Task W10_GetWallets_IncludeArchivedFilter_WorksCorrectly()
    {
        var w1 = await _controller.CreateWallet(new CreateWalletRequest { Name = "Ví 1", Type = 1, InitialBalance = 100_000m }) as ObjectResult;
        var w2 = await _controller.CreateWallet(new CreateWalletRequest { Name = "Ví 2", Type = 2, InitialBalance = 200_000m }) as ObjectResult;

        var dto2 = (WalletDto)w2!.Value!;
        await _controller.DeleteWallet(dto2.Id);

        var defaultListRes = await _controller.GetWallets(includeArchived: false) as OkObjectResult;
        var defaultList = Assert.IsAssignableFrom<List<WalletDto>>(defaultListRes!.Value);
        Assert.Single(defaultList);

        var allListRes = await _controller.GetWallets(includeArchived: true) as OkObjectResult;
        var allList = Assert.IsAssignableFrom<List<WalletDto>>(allListRes!.Value);
        Assert.Equal(2, allList.Count);
    }

    [Fact]
    public async Task W11_AccessOtherUserWallet_Returns404ForGetPutDelete()
    {
        SetCurrentUser(_userB);
        var wB = await _controller.CreateWallet(new CreateWalletRequest { Name = "Ví B", Type = 1, InitialBalance = 100_000m }) as ObjectResult;
        var idB = ((WalletDto)wB!.Value!).Id;

        SetCurrentUser(_userA);
        var getRes = await _controller.GetWallet(idB) as NotFoundObjectResult;
        var putRes = await _controller.UpdateWallet(idB, new UpdateWalletRequest { Name = "Hack" }) as NotFoundObjectResult;
        var delRes = await _controller.DeleteWallet(idB) as NotFoundObjectResult;

        Assert.NotNull(getRes); Assert.Equal(404, getRes.StatusCode);
        Assert.NotNull(putRes); Assert.Equal(404, putRes.StatusCode);
        Assert.NotNull(delRes); Assert.Equal(404, delRes.StatusCode);
    }

    [Fact]
    public async Task W12_PutWallet_CannotModifyBalance_BalanceRemainsUnchanged()
    {
        var res = await _controller.CreateWallet(new CreateWalletRequest { Name = "Ví chính", Type = 1, InitialBalance = 5_000_000m }) as ObjectResult;
        var id = ((WalletDto)res!.Value!).Id;

        var putRes = await _controller.UpdateWallet(id, new UpdateWalletRequest { Name = "Ví đổi tên", Balance = 99_999_999m }) as OkObjectResult;
        Assert.NotNull(putRes);
        var updated = (WalletDto)putRes.Value!;
        Assert.Equal("Ví đổi tên", updated.Name);
        Assert.Equal(5_000_000m, updated.Balance);
    }

    [Fact]
    public async Task W13_PutWallet_ChangeCurrencyWhenBalanceChanged_Returns400()
    {
        var res = await _controller.CreateWallet(new CreateWalletRequest { Name = "Ví VND", Type = 1, InitialBalance = 1_000_000m, Currency = "VND" }) as ObjectResult;
        var id = ((WalletDto)res!.Value!).Id;

        var inDb = await _context.Wallets.FindAsync(id);
        inDb!.Balance = 2_000_000m; // Có biến động số dư
        await _context.SaveChangesAsync();

        var putRes = await _controller.UpdateWallet(id, new UpdateWalletRequest { Name = "Ví VND", Currency = "USD" }) as BadRequestObjectResult;
        Assert.NotNull(putRes);
        Assert.Equal(400, putRes.StatusCode);
    }

    [Fact]
    public async Task W14_DeleteWallet_SoftDeletes_RecordRetainedInDbWithIsArchivedTrue()
    {
        var res = await _controller.CreateWallet(new CreateWalletRequest { Name = "Ví xóa", Type = 1, InitialBalance = 1_000_000m }) as ObjectResult;
        var id = ((WalletDto)res!.Value!).Id;

        var delRes = await _controller.DeleteWallet(id);
        Assert.IsType<OkObjectResult>(delRes);

        var inDb = await _context.Wallets.FindAsync(id);
        Assert.NotNull(inDb);
        Assert.True(inDb.IsArchived);
        Assert.NotNull(inDb.ArchivedAt);
    }

    [Fact]
    public async Task W15_DeleteWallet_AlreadyArchived_DoesNotReturn500()
    {
        var res = await _controller.CreateWallet(new CreateWalletRequest { Name = "Ví lưu trữ", Type = 1, InitialBalance = 100_000m }) as ObjectResult;
        var id = ((WalletDto)res!.Value!).Id;

        await _controller.DeleteWallet(id);
        var secondDelRes = await _controller.DeleteWallet(id) as OkObjectResult;
        Assert.NotNull(secondDelRes);
    }

    [Fact]
    public async Task W16_ConcurrentUpdates_SecondWriterReceivesConcurrencyConflict()
    {
        var res = await _controller.CreateWallet(new CreateWalletRequest { Name = "Ví Concurrency", Type = 1, InitialBalance = 1_000_000m }) as ObjectResult;
        var walletId = ((WalletDto)res!.Value!).Id;

        await using var ctxA = NewContext();
        await using var ctxB = NewContext();

        var wA = await ctxA.Wallets.FindAsync(walletId);
        var wB = await ctxB.Wallets.FindAsync(walletId);

        wA!.Name = "Tên từ Context A";
        await ctxA.SaveChangesAsync();

        wB!.Name = "Tên từ Context B";
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => ctxB.SaveChangesAsync());
    }

    [Fact]
    public async Task W17_RowVersion_AutomaticallyUpdatedOnSaveChanges()
    {
        var res = await _controller.CreateWallet(new CreateWalletRequest { Name = "Ví Version", Type = 1, InitialBalance = 1_000_000m }) as ObjectResult;
        var walletId = ((WalletDto)res!.Value!).Id;

        var w = await _context.Wallets.FindAsync(walletId);
        var initialVersion = (byte[])w!.RowVersion.Clone();

        w.Name = "Ví Version Đổi Tên";
        await _context.SaveChangesAsync();

        var updatedW = await _context.Wallets.FindAsync(walletId);
        Assert.False(initialVersion.SequenceEqual(updatedW!.RowVersion));
    }

    [Fact]
    public async Task W18_GetTotalAssets_CalculatesSumOfActiveWallets_ExcludesArchived()
    {
        await _controller.CreateWallet(new CreateWalletRequest { Name = "VND 1", Type = 1, InitialBalance = 5_000_000m, Currency = "VND" });
        await _controller.CreateWallet(new CreateWalletRequest { Name = "VND 2", Type = 2, InitialBalance = 10_000_000m, Currency = "VND" });
        var res3 = await _controller.CreateWallet(new CreateWalletRequest { Name = "VND Archived", Type = 1, InitialBalance = 3_000_000m, Currency = "VND" }) as ObjectResult;
        await _controller.DeleteWallet(((WalletDto)res3!.Value!).Id);

        var totalRes = await _controller.GetTotalAssets() as OkObjectResult;
        Assert.NotNull(totalRes);
        var total = Assert.IsType<TotalAssetsResponse>(totalRes.Value);
        Assert.Equal(15_000_000m, total.TotalAssetsVnd);
    }

    [Fact]
    public async Task W19_GetTotalAssets_WithForeignCurrency_ConvertsCorrectly()
    {
        await _controller.CreateWallet(new CreateWalletRequest { Name = "Ví VND", Type = 1, InitialBalance = 1_000_000m, Currency = "VND" });
        _context.Wallets.Add(new Wallet { UserId = _userA, Name = "Ví USD", Type = WalletType.Bank, Balance = 100m, InitialBalance = 100m, Currency = "USD" });
        await _context.SaveChangesAsync();

        _profileClient.Rates["USD"] = 25_000m;

        var totalRes = await _controller.GetTotalAssets() as OkObjectResult;
        Assert.NotNull(totalRes);
        var total = Assert.IsType<TotalAssetsResponse>(totalRes.Value);
        Assert.Equal(3_500_000m, total.TotalAssetsVnd);
    }

    [Fact]
    public async Task W20_GetTotalAssets_MissingRate_ExcludesWalletWithout500()
    {
        await _controller.CreateWallet(new CreateWalletRequest { Name = "Ví VND", Type = 1, InitialBalance = 1_000_000m, Currency = "VND" });
        _context.Wallets.Add(new Wallet { UserId = _userA, Name = "Ví USD", Type = WalletType.Bank, Balance = 100m, InitialBalance = 100m, Currency = "USD" });
        await _context.SaveChangesAsync();

        // Không có tỷ giá USD
        _profileClient.Rates.Clear();

        var totalRes = await _controller.GetTotalAssets() as OkObjectResult;
        Assert.NotNull(totalRes);
        var total = Assert.IsType<TotalAssetsResponse>(totalRes.Value);
        Assert.Equal(1_000_000m, total.TotalAssetsVnd);
        Assert.Single(total.ExcludedWallets);
        Assert.Equal("USD", total.ExcludedWallets[0].Currency);
    }

    [Fact]
    public async Task W21_GetTotalAssets_ProfileTimeout_SetsFlagAndReturnsVndSum()
    {
        await _controller.CreateWallet(new CreateWalletRequest { Name = "Ví VND", Type = 1, InitialBalance = 2_000_000m, Currency = "VND" });
        _context.Wallets.Add(new Wallet { UserId = _userA, Name = "Ví EUR", Type = WalletType.Bank, Balance = 100m, InitialBalance = 100m, Currency = "EUR" });
        await _context.SaveChangesAsync();

        _profileClient.SimulateTimeout = true;

        var totalRes = await _controller.GetTotalAssets() as OkObjectResult;
        Assert.NotNull(totalRes);
        var total = Assert.IsType<TotalAssetsResponse>(totalRes.Value);
        Assert.Equal(2_000_000m, total.TotalAssetsVnd);
        Assert.True(total.RatesUnavailable);
    }

    [Fact]
    public async Task W24_CreateOrUpdateWallet_NonVndCurrency_Returns400UnsupportedCurrency()
    {
        // 1. Create with unsupported currency XYZ -> 400 UNSUPPORTED_CURRENCY (FIN-130: USD/EUR now allowed, XYZ rejected)
        var createRes = await _controller.CreateWallet(new CreateWalletRequest
        {
            Name = "Ví XYZ",
            Type = 2,
            InitialBalance = 100m,
            Currency = "XYZ"
        }) as BadRequestObjectResult;

        Assert.NotNull(createRes);
        Assert.Equal(400, createRes.StatusCode);

        // 2. Create USD wallet -> 201 Created (FIN-130 reopen)
        var usdRes = await _controller.CreateWallet(new CreateWalletRequest
        {
            Name = "Ví USD",
            Type = 2,
            InitialBalance = 100m,
            Currency = "USD"
        }) as ObjectResult;
        Assert.NotNull(usdRes);
        Assert.Equal(201, usdRes.StatusCode);

        // 3. Create VND wallet then PUT XYZ -> 400 UNSUPPORTED_CURRENCY
        var vndRes = await _controller.CreateWallet(new CreateWalletRequest
        {
            Name = "Ví VND",
            Type = 1,
            InitialBalance = 0m,
            Currency = "VND"
        }) as ObjectResult;
        Assert.NotNull(vndRes);
        var created = Assert.IsType<WalletDto>(vndRes.Value);

        var putRes = await _controller.UpdateWallet(created.Id, new UpdateWalletRequest
        {
            Name = "Ví VND Sửa",
            Currency = "XYZ"
        }) as BadRequestObjectResult;

        Assert.NotNull(putRes);
        Assert.Equal(400, putRes.StatusCode);
    }

    [Fact]
    public async Task W22_UnauthenticatedRequest_Returns401()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/wallets");
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task W23_Migration_RunsOnEmptyDb_CreatesTablesAndIndexes()
    {
        await using var migConn = new SqliteConnection("Filename=:memory:");
        await migConn.OpenAsync();

        var options = new DbContextOptionsBuilder<WalletDbContext>()
            .UseSqlite(migConn)
            .Options;

        await using var migContext = new WalletDbContext(options);
        await migContext.Database.MigrateAsync();

        // Verify tables exist
        await using var cmd = migConn.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name IN ('Wallets', 'WalletTransfers');";
        await using var reader = await cmd.ExecuteReaderAsync();

        var tables = new List<string>();
        while (await reader.ReadAsync())
        {
            tables.Add(reader.GetString(0));
        }

        Assert.Contains("Wallets", tables);
        Assert.Contains("WalletTransfers", tables);
    }

    [Fact]
    public async Task Deposit_PublisherFailure_RollbacksAndReturns503_GT13()
    {
        var wallet = new Wallet
        {
            Id = Guid.NewGuid(),
            UserId = _userA,
            Name = "Ví tiết kiệm",
            Currency = "VND",
            Balance = 1_000_000m,
            Type = WalletType.Cash,
            RowVersion = Guid.NewGuid().ToByteArray()
        };
        await _context.Wallets.AddAsync(wallet);
        await _context.SaveChangesAsync();

        var goalId = Guid.NewGuid();
        _savingGoalClient.Goals[goalId] = new SavingGoalInfo
        {
            Id = goalId,
            UserId = _userA,
            Name = "Mua máy tính",
            Currency = "VND",
            StatusCode = 1
        };

        _publisher.ShouldThrow = true;

        var key = Guid.NewGuid().ToString();
        var result = await _controller.GoalTransfer(wallet.Id, key, new GoalTransferRequest
        {
            GoalId = goalId,
            Amount = 400_000m
        }) as ObjectResult;

        Assert.NotNull(result);
        Assert.Equal(503, result.StatusCode);

        // Verify wallet balance is unchanged in DB (rollback)
        var refreshed = await _context.Wallets.AsNoTracking().FirstAsync(w => w.Id == wallet.Id);
        Assert.Equal(1_000_000m, refreshed.Balance);
    }

    [Fact]
    public async Task Deposit_DecreasesWalletBalance_Consistent()
    {
        var wallet = new Wallet
        {
            Id = Guid.NewGuid(),
            UserId = _userA,
            Name = "Ví tiền",
            Currency = "VND",
            Balance = 1_000_000m,
            Type = WalletType.Cash,
            RowVersion = Guid.NewGuid().ToByteArray()
        };
        await _context.Wallets.AddAsync(wallet);
        await _context.SaveChangesAsync();

        var goalId = Guid.NewGuid();
        _savingGoalClient.Goals[goalId] = new SavingGoalInfo
        {
            Id = goalId,
            UserId = _userA,
            Name = "Mục tiêu bảo toàn",
            Currency = "VND",
            StatusCode = 1
        };

        var initialBalance = wallet.Balance;
        var depositAmount = 300_000m;

        var key = Guid.NewGuid().ToString();
        var result = await _controller.GoalTransfer(wallet.Id, key, new GoalTransferRequest
        {
            GoalId = goalId,
            Amount = depositAmount
        }) as OkObjectResult;

        Assert.NotNull(result);
        Assert.Equal(200, result.StatusCode);

        var refreshed = await _context.Wallets.AsNoTracking().FirstAsync(w => w.Id == wallet.Id);
        Assert.Equal(initialBalance - depositAmount, refreshed.Balance);
    }

    [Fact]
    public async Task InternalGetWallet_ReturnsMetadata_N23_N25()
    {
        var wallet = new Wallet
        {
            Id = Guid.NewGuid(),
            UserId = _userA,
            Name = "Ví kiểm tra",
            Currency = "USD",
            Balance = 500m,
            Type = WalletType.Cash,
            IsArchived = false,
            RowVersion = Guid.NewGuid().ToByteArray()
        };
        await _context.Wallets.AddAsync(wallet);
        await _context.SaveChangesAsync();

        // Existing wallet returns 200 with metadata
        var result = await _controller.GetInternalWallet(wallet.Id) as OkObjectResult;
        Assert.NotNull(result);
        Assert.Equal(200, result.StatusCode);

        // Non-existing wallet returns 404
        var notFoundResult = await _controller.GetInternalWallet(Guid.NewGuid()) as NotFoundObjectResult;
        Assert.NotNull(notFoundResult);
        Assert.Equal(404, notFoundResult.StatusCode);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}

