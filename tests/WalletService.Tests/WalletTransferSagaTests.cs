using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Events;
using SharedKernel.Events.EventContracts;
using WalletService.Controllers;
using WalletService.Data;
using WalletService.DTOs;
using WalletService.Models;
using WalletService.Services;

namespace WalletService.Tests;

public class WalletTransferSagaTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly WalletDbContext _context;
    private readonly WalletLedger _ledger;
    private readonly TestUserProfileClient _profileClient;
    private readonly TestEventPublisher _publisher;
    private readonly WalletsController _controller;
    private readonly Guid _userA = Guid.NewGuid();
    private readonly Guid _userB = Guid.NewGuid();

    public WalletTransferSagaTests()
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

        _controller = new WalletsController(
            _context,
            _ledger,
            _profileClient,
            NullLogger<WalletsController>.Instance,
            _publisher
        );

        SetCurrentUser(_userA);
    }

    private void SetCurrentUser(Guid userId)
    {
        SetCurrentUser(_controller, userId);
    }

    private static void SetCurrentUser(WalletsController controller, Guid userId)
    {
        var claims = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim("sub", userId.ToString())
        }, "TestAuth"));

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = claims }
        };
    }

    private async Task<(Guid bankId, Guid cashId)> SeedWallets(Guid userId, decimal bank = 10_000_000m, decimal cash = 5_000_000m)
    {
        var bankWallet = new Wallet
        {
            UserId = userId,
            Name = "Ngân hàng " + Guid.NewGuid().ToString("N")[..4],
            Type = WalletType.Bank,
            Balance = bank,
            InitialBalance = bank,
            Currency = "VND"
        };
        var cashWallet = new Wallet
        {
            UserId = userId,
            Name = "Tiền mặt " + Guid.NewGuid().ToString("N")[..4],
            Type = WalletType.Cash,
            Balance = cash,
            InitialBalance = cash,
            Currency = "VND"
        };

        _context.Wallets.AddRange(bankWallet, cashWallet);
        await _context.SaveChangesAsync();

        return (bankWallet.Id, cashWallet.Id);
    }

    private WalletDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<WalletDbContext>()
            .UseSqlite(_connection)
            .Options;
        return new WalletDbContext(options);
    }

    [Fact]
    public async Task T01_Transfer_HappyPath_UpdatesBalancesAndCreatesSuccessRecord()
    {
        var (bank, cash) = await SeedWallets(_userA, 10_000_000m, 5_000_000m);
        var key = Guid.NewGuid().ToString();
        var body = new TransferRequest { FromWalletId = bank, ToWalletId = cash, Amount = 2_000_000m };

        var result = await _controller.Transfer(key, body) as OkObjectResult;

        Assert.NotNull(result);
        var response = Assert.IsType<TransferResponse>(result.Value);
        Assert.Equal(8_000_000m, response.NewSourceBalance);
        Assert.Equal(7_000_000m, response.NewTargetBalance);

        var transferInDb = await _context.WalletTransfers.FindAsync(response.TransferId);
        Assert.NotNull(transferInDb);
        Assert.Equal(TransferStatus.Success, transferInDb.Status);

        // Tổng tài sản không đổi (15M)
        var total = (await _context.Wallets.FindAsync(bank))!.Balance + (await _context.Wallets.FindAsync(cash))!.Balance;
        Assert.Equal(15_000_000m, total);
    }

    [Fact]
    public async Task T02_Transfer_PublishesMoneyTransferredEventOnceWithCorrectPayload()
    {
        var (bank, cash) = await SeedWallets(_userA, 10_000_000m, 5_000_000m);
        var key = Guid.NewGuid().ToString();
        var body = new TransferRequest { FromWalletId = bank, ToWalletId = cash, Amount = 2_000_000m, Fee = 5_000m };

        await _controller.Transfer(key, body);

        var published = Assert.Single(_publisher.Published);
        Assert.Equal("wallet.transferred", published.RoutingKey);
        var evt = Assert.IsType<MoneyTransferredEvent>(published.Event);
        Assert.Equal(_userA, evt.UserId);
        Assert.Equal(bank, evt.FromWalletId);
        Assert.Equal(cash, evt.ToWalletId);
        Assert.Equal(2_000_000m, evt.Amount);
        Assert.Equal(5_000m, evt.Fee);
        Assert.NotEqual(Guid.Empty, evt.Id);
    }

    [Fact]
    public async Task T03_Transfer_SameKeySameBody_ReturnsSameResult_DebitsOnce()
    {
        var (bank, cash) = await SeedWallets(_userA, 10_000_000m, 5_000_000m);
        var key = Guid.NewGuid().ToString();
        var body = new TransferRequest { FromWalletId = bank, ToWalletId = cash, Amount = 2_000_000m };

        var first = await _controller.Transfer(key, body) as OkObjectResult;
        var second = await _controller.Transfer(key, body) as OkObjectResult;

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(((TransferResponse)first.Value!).TransferId, ((TransferResponse)second.Value!).TransferId);
        Assert.Equal(8_000_000m, (await _context.Wallets.FindAsync(bank))!.Balance);
        Assert.Equal(1, await _context.WalletTransfers.CountAsync());
        Assert.Single(_publisher.Published.OfType<(IntegrationEvent, string)>());
    }

    [Fact]
    public async Task T03b_Transfer_ConcurrentDoubleSubmit_SameKey_DoesNotReturn500()
    {
        var (bank, cash) = await SeedWallets(_userA, 10_000_000m, 5_000_000m);
        var key = Guid.NewGuid().ToString();
        var body = new TransferRequest { FromWalletId = bank, ToWalletId = cash, Amount = 2_000_000m };

        var ctx1 = NewContext();
        var ctrl1 = new WalletsController(ctx1, new WalletLedger(ctx1), _profileClient, NullLogger<WalletsController>.Instance, _publisher);
        SetCurrentUser(ctrl1, _userA);

        var ctx2 = NewContext();
        var ctrl2 = new WalletsController(ctx2, new WalletLedger(ctx2), _profileClient, NullLogger<WalletsController>.Instance, _publisher);
        SetCurrentUser(ctrl2, _userA);

        var task1 = ctrl1.Transfer(key, body);
        var task2 = ctrl2.Transfer(key, body);

        var results = await Task.WhenAll(task1, task2);

        foreach (var r in results)
        {
            var status = (r as ObjectResult)?.StatusCode ?? 200;
            Assert.True(status < 500, $"Status was {status}, expected < 500");
        }

        var freshCtx = NewContext();
        var finalBank = await freshCtx.Wallets.FindAsync(bank);
        Assert.Equal(8_000_000m, finalBank!.Balance);
    }

    [Fact]
    public async Task T04_Transfer_SameKeyDifferentBody_Returns422IdempotencyKeyReused()
    {
        var (bank, cash) = await SeedWallets(_userA, 10_000_000m, 5_000_000m);
        var key = Guid.NewGuid().ToString();
        var body1 = new TransferRequest { FromWalletId = bank, ToWalletId = cash, Amount = 2_000_000m };
        var body2 = new TransferRequest { FromWalletId = bank, ToWalletId = cash, Amount = 1_000_000m };

        await _controller.Transfer(key, body1);
        var conflictRes = await _controller.Transfer(key, body2) as ObjectResult;

        Assert.NotNull(conflictRes);
        Assert.Equal(422, conflictRes.StatusCode);
        Assert.Equal(8_000_000m, (await _context.Wallets.FindAsync(bank))!.Balance);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("not-a-guid")]
    public async Task T05_Transfer_MissingOrInvalidIdempotencyKey_Returns400(string? invalidKey)
    {
        var (bank, cash) = await SeedWallets(_userA);
        var body = new TransferRequest { FromWalletId = bank, ToWalletId = cash, Amount = 1_000_000m };

        var res = await _controller.Transfer(invalidKey, body) as BadRequestObjectResult;
        Assert.NotNull(res);
        Assert.Equal(400, res.StatusCode);
        Assert.Equal(10_000_000m, (await _context.Wallets.FindAsync(bank))!.Balance);
    }

    [Fact]
    public async Task T06_Transfer_KeyScopedByUser_DifferentUserCanUseSameKey()
    {
        var (bankA, cashA) = await SeedWallets(_userA);
        var (bankB, cashB) = await SeedWallets(_userB);
        var key = Guid.NewGuid().ToString();

        // User A uses key
        SetCurrentUser(_userA);
        var resA = await _controller.Transfer(key, new TransferRequest { FromWalletId = bankA, ToWalletId = cashA, Amount = 1_000_000m }) as OkObjectResult;
        Assert.NotNull(resA);

        // User B uses same key for the first time
        SetCurrentUser(_userB);
        var resB = await _controller.Transfer(key, new TransferRequest { FromWalletId = bankB, ToWalletId = cashB, Amount = 500_000m }) as OkObjectResult;
        Assert.NotNull(resB);
        Assert.Equal(200, resB.StatusCode);
    }

    [Fact]
    public async Task T07_Transfer_InsufficientBalance_Returns400InsufficientBalance()
    {
        var (bank, cash) = await SeedWallets(_userA, 10_000_000m, 5_000_000m);
        var key = Guid.NewGuid().ToString();
        var body = new TransferRequest { FromWalletId = bank, ToWalletId = cash, Amount = 10_000_001m };

        var res = await _controller.Transfer(key, body) as BadRequestObjectResult;
        Assert.NotNull(res);
        Assert.Equal(400, res.StatusCode);

        Assert.Equal(10_000_000m, (await _context.Wallets.FindAsync(bank))!.Balance);
    }

    [Fact]
    public async Task T08_Transfer_ExactBalanceWithZeroFee_Returns200AndSourceBecomesZero()
    {
        var (bank, cash) = await SeedWallets(_userA, 10_000_000m, 5_000_000m);
        var key = Guid.NewGuid().ToString();
        var body = new TransferRequest { FromWalletId = bank, ToWalletId = cash, Amount = 10_000_000m, Fee = 0 };

        var res = await _controller.Transfer(key, body) as OkObjectResult;
        Assert.NotNull(res);
        Assert.Equal(200, res.StatusCode);

        Assert.Equal(0m, (await _context.Wallets.FindAsync(bank))!.Balance);
        Assert.Equal(15_000_000m, (await _context.Wallets.FindAsync(cash))!.Balance);
    }

    [Fact]
    public async Task T09_Transfer_AmountPlusFeeExceedsBalance_Returns400()
    {
        var (bank, cash) = await SeedWallets(_userA, 10_000_000m, 5_000_000m);
        var key = Guid.NewGuid().ToString();
        var body = new TransferRequest { FromWalletId = bank, ToWalletId = cash, Amount = 9_990_000m, Fee = 20_000m };

        var res = await _controller.Transfer(key, body) as BadRequestObjectResult;
        Assert.NotNull(res);
        Assert.Equal(400, res.StatusCode);
    }

    [Fact]
    public async Task T10_Transfer_FeeIsDeductedFromSourceWallet()
    {
        var (bank, cash) = await SeedWallets(_userA, 10_000_000m, 5_000_000m);
        var key = Guid.NewGuid().ToString();
        var body = new TransferRequest { FromWalletId = bank, ToWalletId = cash, Amount = 1_000_000m, Fee = 10_000m };

        var res = await _controller.Transfer(key, body) as OkObjectResult;
        Assert.NotNull(res);
        var resp = (TransferResponse)res.Value!;

        Assert.Equal(8_990_000m, resp.NewSourceBalance);
        Assert.Equal(6_000_000m, resp.NewTargetBalance);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-1, 0)]
    [InlineData(100, -5)]
    public async Task T11_Transfer_InvalidAmounts_Returns400(decimal amount, decimal fee)
    {
        var (bank, cash) = await SeedWallets(_userA);
        var res = await _controller.Transfer(Guid.NewGuid().ToString(), new TransferRequest { FromWalletId = bank, ToWalletId = cash, Amount = amount, Fee = fee }) as BadRequestObjectResult;
        Assert.NotNull(res);
    }

    [Fact]
    public async Task T11_Transfer_AmountWithThreeDecimals_Returns400()
    {
        var (bank, cash) = await SeedWallets(_userA);
        var res = await _controller.Transfer(Guid.NewGuid().ToString(), new TransferRequest { FromWalletId = bank, ToWalletId = cash, Amount = 100.123m }) as BadRequestObjectResult;
        Assert.NotNull(res);
        Assert.Equal(400, res.StatusCode);
    }

    [Fact]
    public async Task T12_Transfer_SourceSameAsTarget_Returns400()
    {
        var (bank, _) = await SeedWallets(_userA);
        var res = await _controller.Transfer(Guid.NewGuid().ToString(), new TransferRequest { FromWalletId = bank, ToWalletId = bank, Amount = 500_000m }) as BadRequestObjectResult;
        Assert.NotNull(res);
        Assert.Equal(400, res.StatusCode);
    }

    [Fact]
    public async Task T13_Transfer_TargetBelongsToAnotherUser_Returns404()
    {
        var (bankA, _) = await SeedWallets(_userA);
        var (bankB, _) = await SeedWallets(_userB);

        var res = await _controller.Transfer(Guid.NewGuid().ToString(), new TransferRequest { FromWalletId = bankA, ToWalletId = bankB, Amount = 1_000_000m }) as NotFoundObjectResult;
        Assert.NotNull(res);
        Assert.Equal(404, res.StatusCode);
        Assert.Equal(10_000_000m, (await _context.Wallets.FindAsync(bankA))!.Balance);
    }

    [Fact]
    public async Task T14_Transfer_SourceBelongsToAnotherUser_Returns404()
    {
        var (bankA, _) = await SeedWallets(_userA);
        var (bankB, _) = await SeedWallets(_userB);

        var res = await _controller.Transfer(Guid.NewGuid().ToString(), new TransferRequest { FromWalletId = bankB, ToWalletId = bankA, Amount = 1_000_000m }) as NotFoundObjectResult;
        Assert.NotNull(res);
        Assert.Equal(404, res.StatusCode);
    }

    [Fact]
    public async Task T15_Transfer_TargetWalletIsArchived_Returns400()
    {
        var (bank, cash) = await SeedWallets(_userA);
        var cashWallet = await _context.Wallets.FindAsync(cash);
        cashWallet!.IsArchived = true;
        await _context.SaveChangesAsync();

        var res = await _controller.Transfer(Guid.NewGuid().ToString(), new TransferRequest { FromWalletId = bank, ToWalletId = cash, Amount = 1_000_000m }) as BadRequestObjectResult;
        Assert.NotNull(res);
        Assert.Equal(400, res.StatusCode);
    }

    [Fact]
    public async Task T16_Transfer_DifferentCurrencies_Returns400()
    {
        var (bank, cash) = await SeedWallets(_userA);
        var cashWallet = await _context.Wallets.FindAsync(cash);
        cashWallet!.Currency = "USD";
        await _context.SaveChangesAsync();

        var res = await _controller.Transfer(Guid.NewGuid().ToString(), new TransferRequest { FromWalletId = bank, ToWalletId = cash, Amount = 100_000m }) as BadRequestObjectResult;
        Assert.NotNull(res);
        Assert.Equal(400, res.StatusCode);
    }

    [Fact]
    public async Task T17_Transfer_CreditStepFailure_CompensatesAndRefundsSource()
    {
        var (bank, cash) = await SeedWallets(_userA, 10_000_000m, 5_000_000m);
        var failingLedger = new FailingCreditLedger();
        var controllerWithFailingLedger = new WalletsController(
            _context,
            failingLedger,
            _profileClient,
            NullLogger<WalletsController>.Instance,
            _publisher
        );
        var claims = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, _userA.ToString()),
            new Claim("sub", _userA.ToString())
        }, "TestAuth"));
        controllerWithFailingLedger.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = claims } };

        var res = await controllerWithFailingLedger.Transfer(Guid.NewGuid().ToString(), new TransferRequest { FromWalletId = bank, ToWalletId = cash, Amount = 2_000_000m }) as ObjectResult;

        Assert.NotNull(res);
        Assert.Equal(500, res.StatusCode);

        // Nguồn được hoàn lại 10M, đích vẫn 5M
        var bankW = await _context.Wallets.FindAsync(bank);
        var cashW = await _context.Wallets.FindAsync(cash);
        Assert.Equal(10_000_000m, bankW!.Balance);
        Assert.Equal(5_000_000m, cashW!.Balance);

        // Bản ghi chuyển tiền có Status = FailedCompensated
        var transfer = await _context.WalletTransfers.FirstOrDefaultAsync(t => t.SourceWalletId == bank);
        Assert.NotNull(transfer);
        Assert.Equal(TransferStatus.FailedCompensated, transfer.Status);

        // Không publish MoneyTransferred event
        Assert.Empty(_publisher.Published);
    }

    [Fact]
    public async Task T18_Transfer_WithOutbox_CommitsTransferAndOutboxWhenBrokerDown()
    {
        var (bank, cash) = await SeedWallets(_userA, 10_000_000m, 5_000_000m);
        var outboxPublisher = new SharedKernel.Messaging.OutboxEventPublisher<WalletDbContext>(_context);
        var controllerWithOutbox = new WalletsController(
            _context,
            _ledger,
            _profileClient,
            NullLogger<WalletsController>.Instance,
            outboxPublisher
        );
        var claims = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, _userA.ToString()),
            new Claim("sub", _userA.ToString())
        }, "TestAuth"));
        controllerWithOutbox.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = claims } };

        var res = await controllerWithOutbox.Transfer(Guid.NewGuid().ToString(), new TransferRequest { FromWalletId = bank, ToWalletId = cash, Amount = 2_000_000m }) as OkObjectResult;

        Assert.NotNull(res);
        Assert.Equal(200, res.StatusCode);

        var bankW = await _context.Wallets.FindAsync(bank);
        Assert.Equal(8_000_000m, bankW!.Balance);
        var outbox = await _context.OutboxMessages.SingleAsync();
        Assert.Equal("wallet.transferred", outbox.RoutingKey);
        Assert.Null(outbox.SentAt);
    }

    [Fact]
    public async Task T19_ConcurrentDebits_SecondWriterGetsConflict_BalanceNeverNegative()
    {
        var (bank, cash) = await SeedWallets(_userA, 10_000_000m, 5_000_000m);
        await using var ctxA = NewContext();
        await using var ctxB = NewContext();

        var wA = await ctxA.Wallets.FindAsync(bank);
        var wB = await ctxB.Wallets.FindAsync(bank); // Cả 2 cùng đọc 10M

        wA!.Balance -= 6_000_000m;
        await ctxA.SaveChangesAsync(); // A trừ 6M thành 4M

        wB!.Balance -= 6_000_000m;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => ctxB.SaveChangesAsync());

        await using var checkCtx = NewContext();
        var finalW = await checkCtx.Wallets.FindAsync(bank);
        Assert.Equal(4_000_000m, finalW!.Balance);
    }

    [Fact]
    public async Task T20_TransferThenArchiveTarget_ArchiveSucceedsAndTransferRecordRetained()
    {
        var (bank, cash) = await SeedWallets(_userA, 10_000_000m, 5_000_000m);
        var res = await _controller.Transfer(Guid.NewGuid().ToString(), new TransferRequest { FromWalletId = bank, ToWalletId = cash, Amount = 2_000_000m }) as OkObjectResult;
        Assert.NotNull(res);

        var archiveRes = await _controller.DeleteWallet(cash) as OkObjectResult;
        Assert.NotNull(archiveRes);

        var transferInDb = await _context.WalletTransfers.FirstOrDefaultAsync(t => t.TargetWalletId == cash);
        Assert.NotNull(transferInDb);
    }

    [Fact]
    public async Task T22_Transfer_NoteExceeds500Chars_Returns400()
    {
        var (bank, cash) = await SeedWallets(_userA);
        var longNote = new string('N', 501);

        var res = await _controller.Transfer(Guid.NewGuid().ToString(), new TransferRequest { FromWalletId = bank, ToWalletId = cash, Amount = 1_000_000m, Note = longNote }) as BadRequestObjectResult;
        Assert.NotNull(res);
        Assert.Equal(400, res.StatusCode);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}

public class FailingPublisher : SharedKernel.Messaging.IEventPublisher
{
    public Task PublishAsync<T>(T @event, string routingKey, CancellationToken cancellationToken = default) where T : SharedKernel.Events.IntegrationEvent
    {
        throw new InvalidOperationException("Simulated broker down");
    }
}
