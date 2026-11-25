using Microsoft.EntityFrameworkCore;
using SharedKernel.Extensions;
using SharedKernel.Messaging;
using StackExchange.Redis;
using ReportService.Consumers;
using ReportService.Data;
using ReportService.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Database SQLite
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Data Source=../../../database/report.db;Cache=Shared";
builder.Services.AddDbContext<ReportDbContext>(options =>
{
    options.UseSqlite(connectionString);
});

builder.Services.AddSingleton(TimeProvider.System);

// Redis Cache (fail-safe: AbortOnConnectFail=false -> Redis lỗi thì tính trực tiếp theo D59)
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var redisConn = builder.Configuration["Redis:Configuration"] ?? "localhost:6379";
    var opts = ConfigurationOptions.Parse(redisConn);
    opts.AbortOnConnectFail = false;
    opts.ConnectTimeout = 1000;
    opts.SyncTimeout = 1000;
    return ConnectionMultiplexer.Connect(opts);
});
builder.Services.AddSingleton<IReportCacheService, RedisReportCacheService>();

// External Service Clients
builder.Services.AddHttpClient<IExchangeRateClient, ExchangeRateClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:UserProfileService:Url"] ?? "http://localhost:8002");
    client.Timeout = TimeSpan.FromSeconds(2);
});
builder.Services.AddHttpClient<IBudgetClient, BudgetClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:BudgetService:Url"] ?? "http://localhost:8006");
    client.Timeout = TimeSpan.FromSeconds(2.5);
});
builder.Services.AddHttpClient<ICategoryLookupClient, CategoryLookupClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:CategoryService:Url"] ?? "http://localhost:8005");
    client.Timeout = TimeSpan.FromSeconds(2);
});
builder.Services.AddHttpClient<IWalletLookupClient, WalletLookupClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:WalletService:Url"] ?? "http://localhost:8003");
    client.Timeout = TimeSpan.FromSeconds(2);
});
builder.Services.AddHttpClient<ITransactionBackfillClient, TransactionBackfillClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:TransactionService:Url"] ?? "http://localhost:8004");
    client.Timeout = TimeSpan.FromSeconds(10);
});

// Application Services
builder.Services.AddScoped<IReportQueryService, ReportQueryService>();
builder.Services.AddScoped<IBackfillService, BackfillService>();
builder.Services.AddSingleton<IExportGenerator, ExportGenerator>();
builder.Services.AddSingleton<IEventPublisher, RabbitMqEventPublisher>();

// Background Workers & Consumers
builder.Services.AddHostedService<TransactionCreatedConsumer>();
builder.Services.AddHostedService<TransactionUpdatedConsumer>();
builder.Services.AddHostedService<TransactionDeletedConsumer>();
builder.Services.AddHostedService<CategoryUpdatedConsumer>();
builder.Services.AddHostedService<ExportWorkerBackgroundService>();
builder.Services.AddHostedService<BackfillHostedService>();

// Khai báo RabbitMQ Topology
try
{
    var rmqHost = builder.Configuration["RabbitMQ:Host"] ?? "localhost";
    var factory = new RabbitMQ.Client.ConnectionFactory { HostName = rmqHost };
    using var conn = factory.CreateConnection();
    using var ch = conn.CreateModel();
    RabbitMqTopology.DeclareConsumerQueue(ch, "report.transaction-created", "transaction.created");
    RabbitMqTopology.DeclareConsumerQueue(ch, "report.transaction-updated", "transaction.updated");
    RabbitMqTopology.DeclareConsumerQueue(ch, "report.transaction-deleted", "transaction.deleted");
    RabbitMqTopology.DeclareConsumerQueue(ch, "report.category-updated", "category.updated");
}
catch (Exception)
{
    // Bỏ qua nếu RabbitMQ chưa sẵn sàng
}

// JWT & Consul & Health/Metrics (D61)
builder.Services.AddJwtAuthentication(builder.Configuration);
builder.Services.AddConsulConfig(builder.Configuration);
builder.Services.AddServiceHealthChecks<ReportDbContext>(builder.Configuration, checkRabbitMq: true, checkRedis: true);

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ReportDbContext>();
    var conn = db.Database.GetDbConnection();
    if (!string.IsNullOrEmpty(conn.DataSource) && Path.GetDirectoryName(conn.DataSource) is { Length: > 0 } dir)
    {
        Directory.CreateDirectory(dir);
    }
    db.Database.Migrate();
    try
    {
        db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
    }
    catch { }
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();
app.UseAuthorization();

app.UseServiceHealthAndMetrics("ReportService");
app.UseConsulServiceRegistration(builder.Configuration, app.Lifetime);

app.MapControllers();

app.Run();

public partial class Program { }
