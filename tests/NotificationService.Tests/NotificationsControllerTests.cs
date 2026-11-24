using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.DTOs;
using SharedKernel.Events.EventContracts;
using NotificationService.Consumers;
using NotificationService.Controllers;
using NotificationService.Data;
using NotificationService.DTOs;
using NotificationService.Models;

namespace NotificationService.Tests;

public class NotificationsControllerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly NotificationDbContext _context;
    private readonly NotificationsController _controller;
    private readonly BudgetThresholdReachedConsumer _consumer;
    private readonly Guid _userId = Guid.NewGuid();

    public NotificationsControllerTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<NotificationDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new NotificationDbContext(options);
        _context.Database.EnsureCreated();

        _controller = new NotificationsController(_context, NullLogger<NotificationsController>.Instance);

        var services = new ServiceCollection();
        services.AddSingleton(_context);
        var serviceProvider = services.BuildServiceProvider();

        var inMemoryConfig = new Dictionary<string, string?>
        {
            ["RabbitMQ:Host"] = "localhost"
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(inMemoryConfig).Build();

        _consumer = new BudgetThresholdReachedConsumer(
            configuration,
            NullLogger<BudgetThresholdReachedConsumer>.Instance,
            serviceProvider);

        SetCurrentUser(_userId);
    }

    private void SetCurrentUser(Guid userId)
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim("sub", userId.ToString())
        }, "TestAuth"));

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task N01_BudgetThresholdReached_CreatesInAppNotification()
    {
        var alertEvent = new BudgetThresholdReachedEvent
        {
            BudgetId = Guid.NewGuid(),
            UserId = _userId,
            CategoryId = Guid.NewGuid(),
            CategoryName = "Ăn uống",
            Percent = 80,
            CurrentSpent = 1600000m,
            BudgetLimit = 2000000m,
            Month = 11,
            Year = 2026
        };

        await _consumer.HandleEventAsync(alertEvent);

        var result = await _controller.GetNotifications(new NotificationQueryParameters());
        var ok = Assert.IsType<OkObjectResult>(result);
        var paged = Assert.IsType<PagedResult<NotificationDto>>(ok.Value);

        Assert.Single(paged.Items);
        var item = paged.Items[0];
        Assert.Equal("BudgetAlert", item.Type);
        Assert.Contains("Ăn uống", item.Title);
        Assert.Contains("80%", item.Title);
        Assert.False(item.IsRead);
    }

    [Fact]
    public async Task N02_DuplicateEventId_IsIdempotent()
    {
        var eventId = Guid.NewGuid();
        var alertEvent = new BudgetThresholdReachedEvent
        {
            BudgetId = Guid.NewGuid(),
            UserId = _userId,
            CategoryId = Guid.NewGuid(),
            CategoryName = "Ăn uống",
            Percent = 80,
            CurrentSpent = 1600000m,
            BudgetLimit = 2000000m,
            Month = 11,
            Year = 2026
        };
        // Reflection gán Id
        typeof(SharedKernel.Events.IntegrationEvent).GetProperty("Id")?.SetValue(alertEvent, eventId);

        // Giao 2 lần
        await _consumer.HandleEventAsync(alertEvent);
        await _consumer.HandleEventAsync(alertEvent);

        var count = await _context.Notifications.CountAsync(n => n.EventId == eventId);
        Assert.Equal(1, count); // Idempotent
    }

    [Fact]
    public async Task N03_GetNotifications_IdorProtection_ReturnsOnlyCurrentUser()
    {
        var otherUser = Guid.NewGuid();
        _context.Notifications.AddRange(
            new Notification
            {
                UserId = _userId,
                Title = "Thông báo của User A",
                Message = "Message A",
                CreatedAt = DateTime.UtcNow
            },
            new Notification
            {
                UserId = otherUser,
                Title = "Thông báo của User B",
                Message = "Message B",
                CreatedAt = DateTime.UtcNow
            });
        await _context.SaveChangesAsync();

        var result = await _controller.GetNotifications(new NotificationQueryParameters());
        var ok = Assert.IsType<OkObjectResult>(result);
        var paged = Assert.IsType<PagedResult<NotificationDto>>(ok.Value);

        Assert.Single(paged.Items);
        Assert.Equal("Thông báo của User A", paged.Items[0].Title);
    }

    [Fact]
    public async Task N04_GetNotifications_PaginationAndNewestFirst()
    {
        for (int i = 1; i <= 5; i++)
        {
            _context.Notifications.Add(new Notification
            {
                UserId = _userId,
                Title = $"Thông báo số {i}",
                Message = $"Nội dung {i}",
                CreatedAt = DateTime.UtcNow.AddMinutes(i)
            });
        }
        await _context.SaveChangesAsync();

        var result = await _controller.GetNotifications(new NotificationQueryParameters { Page = 1, PageSize = 3 });
        var ok = Assert.IsType<OkObjectResult>(result);
        var paged = Assert.IsType<PagedResult<NotificationDto>>(ok.Value);

        Assert.Equal(5, paged.TotalCount);
        Assert.Equal(3, paged.Items.Count);
        // Sắp xếp mới nhất trước
        Assert.Equal("Thông báo số 5", paged.Items[0].Title);
        Assert.Equal("Thông báo số 4", paged.Items[1].Title);
        Assert.Equal("Thông báo số 3", paged.Items[2].Title);
    }

    [Fact]
    public async Task N05_MarkAsRead_UpdatesStatus()
    {
        var notif = new Notification
        {
            UserId = _userId,
            Title = "Chưa đọc",
            Message = "Test",
            IsRead = false,
            CreatedAt = DateTime.UtcNow
        };
        _context.Notifications.Add(notif);
        await _context.SaveChangesAsync();

        var result = await _controller.MarkAsRead(notif.Id);
        Assert.IsType<OkObjectResult>(result);

        var updated = await _context.Notifications.FindAsync(notif.Id);
        Assert.True(updated!.IsRead);
    }
}
