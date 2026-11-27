using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SharedKernel.Extensions;
using SharedKernel.Messaging;
using BudgetService.Consumers;
using BudgetService.Data;
using BudgetService.Services;

var builder = WebApplication.CreateBuilder(args);

builder.AddSmartWalletLogging("BudgetService");

// Cấu hình Controllers
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// Cấu hình Swagger
builder.Services.AddSwaggerGen();

// Cấu hình Database SQLite
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
    ?? "Data Source=../../../database/budget.db;Cache=Shared";
builder.Services.AddDbContext<BudgetDbContext>(options =>
{
    options.UseSqlite(connectionString);
    options.AddInterceptors(new BudgetSqlitePragmaInterceptor());
});

// Đăng ký Ledger Service
builder.Services.AddScoped<IBudgetLedgerService, BudgetLedgerService>();

// Đăng ký HTTP Clients gọi CategoryService và TransactionService với Timeout và Polly
builder.Services.AddHttpClient<ICategoryClient, CategoryClient>(client =>
{
    var url = builder.Configuration["Services:CategoryService:Url"] ?? "http://localhost:8005";
    client.BaseAddress = new Uri(url);
    client.Timeout = TimeSpan.FromSeconds(3); // D8: timeout 3s
});

builder.Services.AddHttpClient<ITransactionClient, TransactionClient>(client =>
{
    var url = builder.Configuration["Services:TransactionService:Url"] ?? "http://localhost:8004";
    client.BaseAddress = new Uri(url);
    client.Timeout = TimeSpan.FromSeconds(5);
});

builder.Services.AddSmartWalletOutbox<BudgetDbContext>();

// Đăng ký RabbitMQ Consumers cho các sự kiện giao dịch & danh mục
builder.Services.AddHostedService<TransactionCreatedConsumer>();
builder.Services.AddHostedService<TransactionUpdatedConsumer>();
builder.Services.AddHostedService<TransactionDeletedConsumer>();
builder.Services.AddHostedService<CategoryUpdatedConsumer>();

// Khai báo topology queue giao dịch
try
{
    var rmqHost = builder.Configuration["RabbitMQ:Host"] ?? "localhost";
    var factory = new RabbitMQ.Client.ConnectionFactory { HostName = rmqHost };
    using var conn = factory.CreateConnection();
    using var ch = conn.CreateModel();
    RabbitMqTopology.DeclareConsumerQueue(ch, "budget.transaction-created", "transaction.created");
    RabbitMqTopology.DeclareConsumerQueue(ch, "budget.transaction-updated", "transaction.updated");
    RabbitMqTopology.DeclareConsumerQueue(ch, "budget.transaction-deleted", "transaction.deleted");
    RabbitMqTopology.DeclareConsumerQueue(ch, "budget.category-updated", "category.updated");
}
catch (Exception)
{
    // Bỏ qua nếu RabbitMQ chưa sẵn sàng ở môi trường test không có broker
}

// Cấu hình JWT Bearer chung từ SharedKernel
builder.Services.AddJwtAuthentication(builder.Configuration);

// Cấu hình Consul Service Discovery từ SharedKernel
builder.Services.AddConsulConfig(builder.Configuration);

// Chuẩn hóa Health Checks & Prometheus Metrics (D61)
builder.Services.AddServiceHealthChecks<BudgetDbContext>(builder.Configuration, checkRabbitMq: true);

var app = builder.Build();

// Khởi tạo Database nếu cần & áp dụng Migrations
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<BudgetDbContext>();
    var conn = context.Database.GetDbConnection();
    if (!string.IsNullOrEmpty(conn.DataSource) && Path.GetDirectoryName(conn.DataSource) is { Length: > 0 } dir)
    {
        Directory.CreateDirectory(dir);
    }
    context.Database.Migrate();

    // Bật WAL mode cho SQLite
    try
    {
        context.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
    }
    catch { }
}

app.UseSmartWalletOpenApi();

app.UseSmartWalletRequestLogging();

app.UseAuthentication();
app.UseAuthorization();

// Endpoint kiểm tra sức khỏe & metrics (D61)
app.UseServiceHealthAndMetrics("BudgetService");

// Đăng ký dịch vụ với Consul
app.UseConsulServiceRegistration(builder.Configuration, app.Lifetime);

app.MapControllers();

app.Run();

sealed class BudgetSqlitePragmaInterceptor : DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA busy_timeout=5000;";
        cmd.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA busy_timeout=5000;";
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}
