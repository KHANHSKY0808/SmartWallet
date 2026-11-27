using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
    private class FakeRawPublisher : IRawEventPublisher
    {
        public bool ShouldThrow { get; set; } = true;
        public List<(Guid Id, string RoutingKey, string Payload)> Sent { get; } = new();
        public Task PublishRawAsync(Guid eventId, string eventType, string routingKey, string payloadJson, string? correlationId = null, DateTime? occurredAtUtc = null, CancellationToken cancellationToken = default)
        {
            if (ShouldThrow)
            {
                throw new InvalidOperationException("RabbitMQ connection refused (simulated D72 test)");
            }
            Sent.Add((eventId, routingKey, payloadJson));
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
    private readonly FakeRawPublisher _rawPublisher;
    private readonly OutboxDispatcher<RecurringDebtDbContext> _dispatcher;
    private readonly ServiceProvider _sp;
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

        var wakeSignal = new OutboxWakeSignal();
        var outboxPublisher = new OutboxEventPublisher<RecurringDebtDbContext>(_context, wakeSignal);
        _rawPublisher = new FakeRawPublisher();
        _sp = new ServiceCollection().AddSingleton(_context).BuildServiceProvider();
        _dispatcher = new OutboxDispatcher<RecurringDebtDbContext>(_sp, _rawPublisher, wakeSignal, NullLogger<OutboxDispatcher<RecurringDebtDbContext>>.Instance);

        _walletClient = new FakeWalletClient();
        _walletClient.Wallets[_walletA] = new WalletValidationResult(true, _userA, false, "VND");

        _controller = new DebtsController(
            _context,
            new FixedSchedulerEngine(),
            _walletClient,
            NullLogger<DebtsController>.Instance,
            outboxPublisher);

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
        _sp.Dispose();
        _context.Dispose();
        _connection.Close();
        _connection.Dispose();
    }

    [Fact]
    public async Task Test_N37_CreateDebt_WithOutbox_CommitsDebtAndOutboxWhenBrokerDown()
    {
        _rawPublisher.ShouldThrow = true;

        var result = await _controller.CreateDebt(new CreateDebtRequest
        {
            Type = 1,
            PersonName = "Anh Nam",
            Amount = 1_000_000m,
            DueDate = new DateOnly(2027, 2, 10),
            WalletId = _walletA
        }, CancellationToken.None) as ObjectResult;

        Assert.NotNull(result);
        Assert.Equal(201, result.StatusCode);
        Assert.Single(await _context.Debts.ToListAsync());
        var outbox = await _context.OutboxMessages.SingleAsync();
        Assert.Equal("debt.transaction_requested", outbox.RoutingKey);
        Assert.Null(outbox.SentAt);

        _rawPublisher.ShouldThrow = false;
        Assert.Equal(1, await _dispatcher.ProcessPendingAsync());
        Assert.Single(_rawPublisher.Sent);
    }

    [Fact]
    public async Task Test_N37_MakePayment_WithOutbox_CommitsPaymentAndOutboxWhenBrokerDown()
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

        _rawPublisher.ShouldThrow = true;
        SetUser(_userA, Guid.NewGuid().ToString());

        var result = await _controller.MakePayment(debt.Id, new CreateDebtPaymentRequest
        {
            Amount = 2_000_000m,
            WalletId = _walletA
        }, CancellationToken.None) as ObjectResult;

        Assert.NotNull(result);
        Assert.Equal(201, result.StatusCode);

        _context.ChangeTracker.Clear();
        var refreshed = await _context.Debts.FirstAsync(d => d.Id == debt.Id);
        Assert.Equal(2_000_000m, refreshed.PaidAmount);
        Assert.Single(await _context.DebtPayments.ToListAsync());
        var outbox = await _context.OutboxMessages.SingleAsync();
        Assert.Equal("debt.transaction_requested", outbox.RoutingKey);
        Assert.Null(outbox.SentAt);
    }

    [Fact]
    public async Task Test_N37_DeleteDebt_WithOutbox_CommitsDeleteAndOutboxWhenBrokerDown()
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

        _rawPublisher.ShouldThrow = true;
        var result = await _controller.DeleteDebt(debt.Id, CancellationToken.None) as OkObjectResult;

        Assert.NotNull(result);
        Assert.Equal(200, result.StatusCode);

        _context.ChangeTracker.Clear();
        Assert.Null(await _context.Debts.FirstOrDefaultAsync(d => d.Id == debt.Id));
        var outbox = await _context.OutboxMessages.SingleAsync();
        Assert.Equal("debt.deleted", outbox.RoutingKey);
        Assert.Null(outbox.SentAt);
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

