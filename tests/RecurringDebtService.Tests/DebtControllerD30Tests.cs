using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RecurringDebtService.Controllers;
using RecurringDebtService.Data;
using RecurringDebtService.DTOs;
using RecurringDebtService.Models;
using RecurringDebtService.Services;
using SharedKernel.Events;
using SharedKernel.Messaging;

namespace RecurringDebtService.Tests;

public class DebtControllerD30Tests : IDisposable
{
    private class FailingEventPublisher : IEventPublisher
    {
        public bool ShouldThrow { get; set; } = true;
        public Task PublishAsync<TEvent>(TEvent @event, string routingKey, CancellationToken cancellationToken = default)
            where TEvent : IntegrationEvent
        {
            if (ShouldThrow)
            {
                throw new InvalidOperationException("RabbitMQ connection refused (simulated D30 test)");
            }
            return Task.CompletedTask;
        }
    }

    private class FakeWalletClient : IWalletServiceClient
    {
        public Dictionary<Guid, WalletValidationResult> Wallets { get; } = new();

        public Task<WalletValidationResult> ValidateWalletAsync(Guid walletId, string? bearerToken = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Wallets.TryGetValue(walletId, out var res)
                ? res
                : new WalletValidationResult(false, null, false, "VND"));
        }
    }

    private class FixedSchedulerEngine : ISchedulerEngine
    {
        public DateOnly GetTodayVietnam() => new(2027, 1, 1);
        public DateOnly ComputeNextOccurrence(DateOnly current, FrequencyType freq, int anchorDay, DateOnly startDate) => current.AddMonths(1);
        public DateOnly FindInitialNextExecutionDate(DateOnly startDate, FrequencyType freq, int anchorDay, DateOnly today) => startDate;
        public Task RunSweepAsync(DateOnly asOfDate, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private readonly SqliteConnection _connection;
    private readonly RecurringDebtDbContext _context;
    private readonly FailingEventPublisher _publisher;
    private readonly FakeWalletClient _walletClient;
    private readonly DebtsController _controller;
    private readonly Guid _userA = Guid.NewGuid();
    private readonly Guid _walletA = Guid.NewGuid();

    public DebtControllerD30Tests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<RecurringDebtDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new RecurringDebtDbContext(options);
        _context.Database.EnsureCreated();

        _publisher = new FailingEventPublisher();
        _walletClient = new FakeWalletClient();
        _walletClient.Wallets[_walletA] = new WalletValidationResult(true, _userA, false, "VND");

        _controller = new DebtsController(
            _context,
            new FixedSchedulerEngine(),
            _walletClient,
            NullLogger<DebtsController>.Instance,
            _publisher);

        SetUser(_userA);
    }

    private void SetUser(Guid userId, string? idempotencyKey = null)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString())
        }, "TestAuth"));
        httpContext.Request.Headers["Authorization"] = "Bearer test-token";
        if (!string.IsNullOrEmpty(idempotencyKey))
        {
            httpContext.Request.Headers["Idempotency-Key"] = idempotencyKey;
        }
        _controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Close();
        _connection.Dispose();
    }

    [Fact]
    public async Task Test_N37_CreateDebt_PublisherThrows_Returns503AndRollsBack()
    {
        _publisher.ShouldThrow = true;

        var result = await _controller.CreateDebt(new CreateDebtRequest
        {
            Type = 1,
            PersonName = "Anh Nam",
            Amount = 1_000_000m,
            DueDate = new DateOnly(2027, 2, 10),
            WalletId = _walletA
        }, CancellationToken.None) as ObjectResult;

        Assert.NotNull(result);
        Assert.Equal(503, result.StatusCode);
        Assert.Empty(await _context.Debts.ToListAsync());
    }

    [Fact]
    public async Task Test_N37_MakePayment_PublisherThrows_Returns503AndRollsBack()
    {
        var debt = new Debt
        {
            Id = Guid.NewGuid(),
            UserId = _userA,
            Type = DebtType.Lend,
            PersonName = "Anh Nam",
            Amount = 5_000_000m,
            PaidAmount = 0m,
            Currency = "VND",
            DueDate = new DateOnly(2027, 2, 10),
            Status = DebtStatus.Active,
            WalletId = _walletA,
            RowVersion = Guid.NewGuid()
        };
        _context.Debts.Add(debt);
        await _context.SaveChangesAsync();

        _publisher.ShouldThrow = true;
        SetUser(_userA, Guid.NewGuid().ToString());

        var result = await _controller.MakePayment(debt.Id, new CreateDebtPaymentRequest
        {
            Amount = 2_000_000m,
            WalletId = _walletA
        }, CancellationToken.None) as ObjectResult;

        Assert.NotNull(result);
        Assert.Equal(503, result.StatusCode);

        _context.ChangeTracker.Clear();
        var refreshed = await _context.Debts.FirstAsync(d => d.Id == debt.Id);
        Assert.Equal(0m, refreshed.PaidAmount);
        Assert.Empty(await _context.DebtPayments.ToListAsync());
        Assert.Empty(await _context.IdempotencyRecords.ToListAsync());
    }

    [Fact]
    public async Task Test_N37_DeleteDebt_PublisherThrows_Returns503AndRollsBack()
    {
        var debt = new Debt
        {
            Id = Guid.NewGuid(),
            UserId = _userA,
            Type = DebtType.Lend,
            PersonName = "Anh Nam",
            Amount = 5_000_000m,
            PaidAmount = 0m,
            Currency = "VND",
            DueDate = new DateOnly(2027, 2, 10),
            Status = DebtStatus.Active,
            WalletId = _walletA,
            RowVersion = Guid.NewGuid()
        };
        _context.Debts.Add(debt);
        await _context.SaveChangesAsync();

        _publisher.ShouldThrow = true;
        var result = await _controller.DeleteDebt(debt.Id, CancellationToken.None) as ObjectResult;

        Assert.NotNull(result);
        Assert.Equal(503, result.StatusCode);

        _context.ChangeTracker.Clear();
        Assert.NotNull(await _context.Debts.FirstOrDefaultAsync(d => d.Id == debt.Id));
    }

    [Fact]
    public async Task Test_N34_N35_MakePayment_WrongOwnerOrCurrencyMismatch_Rejected()
    {
        var debt = new Debt
        {
            Id = Guid.NewGuid(),
            UserId = _userA,
            Type = DebtType.Lend,
            PersonName = "Anh Nam",
            Amount = 100m,
            PaidAmount = 0m,
            Currency = "USD",
            DueDate = new DateOnly(2027, 2, 10),
            Status = DebtStatus.Active,
            WalletId = _walletA,
            RowVersion = Guid.NewGuid()
        };
        _context.Debts.Add(debt);
        await _context.SaveChangesAsync();

        // N34: Ví của user B
        var walletB = Guid.NewGuid();
        _walletClient.Wallets[walletB] = new WalletValidationResult(true, Guid.NewGuid(), false, "USD");
        SetUser(_userA, Guid.NewGuid().ToString());
        var resWrongOwner = await _controller.MakePayment(debt.Id, new CreateDebtPaymentRequest
        {
            Amount = 50m,
            WalletId = walletB
        }, CancellationToken.None) as NotFoundObjectResult;
        Assert.NotNull(resWrongOwner);
        Assert.Equal(404, resWrongOwner.StatusCode);

        // N35: Ví VND của user A thanh toán cho nợ USD -> 400 CURRENCY_MISMATCH
        SetUser(_userA, Guid.NewGuid().ToString());
        var resCurrency = await _controller.MakePayment(debt.Id, new CreateDebtPaymentRequest
        {
            Amount = 50m,
            WalletId = _walletA
        }, CancellationToken.None) as BadRequestObjectResult;
        Assert.NotNull(resCurrency);
        Assert.Equal(400, resCurrency.StatusCode);
        Assert.Contains("CURRENCY_MISMATCH", System.Text.Json.JsonSerializer.Serialize(resCurrency.Value));
    }
}

