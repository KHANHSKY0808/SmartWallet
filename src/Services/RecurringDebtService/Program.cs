using Microsoft.EntityFrameworkCore;
using SharedKernel.Extensions;
using SharedKernel.Messaging;
using RecurringDebtService.Consumers;
using RecurringDebtService.Data;
using RecurringDebtService.Services;

var builder = WebApplication.CreateBuilder(args);

// Cấu hình Controllers
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// Cấu hình Swagger
builder.Services.AddSwaggerGen();

// Cấu hình SQLite
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
    ?? "Data Source=../../../database/recurringdebtservice.db;Cache=Shared";
builder.Services.AddDbContext<RecurringDebtDbContext>(options =>
{
    options.UseSqlite(connectionString);
});

// Cấu hình TimeProvider (injectable cho unit test - D54)
builder.Services.AddSingleton(TimeProvider.System);

// Cấu hình Scheduler Engine
builder.Services.AddScoped<ISchedulerEngine, SchedulerEngine>();

// Cấu hình HttpClients để xác thực Ví và Danh mục
builder.Services.AddHttpClient<IWalletServiceClient, WalletServiceClient>(client =>
{
    var url = builder.Configuration["Services:WalletService:Url"] ?? "http://localhost:8003";
    client.BaseAddress = new Uri(url);
    client.Timeout = TimeSpan.FromSeconds(3);
}).AddHttpMessageHandler(sp => new SystemTokenDelegatingHandler(sp.GetRequiredService<IConfiguration>(), "RecurringDebtService"));

builder.Services.AddHttpClient<ICategoryServiceClient, CategoryServiceClient>(client =>
{
    var url = builder.Configuration["Services:CategoryService:Url"] ?? "http://localhost:8005";
    client.BaseAddress = new Uri(url);
    client.Timeout = TimeSpan.FromSeconds(3);
}).AddHttpMessageHandler(sp => new SystemTokenDelegatingHandler(sp.GetRequiredService<IConfiguration>(), "RecurringDebtService"));

// Cấu hình Event Publisher & Consumers
builder.Services.AddSingleton<IEventPublisher, RabbitMqEventPublisher>();
builder.Services.AddHostedService<RecurringTransactionFailedConsumer>();
builder.Services.AddHostedService<RecurringSchedulerBackgroundService>();

// Khai báo topology queue
try
{
    var rmqHost = builder.Configuration["RabbitMQ:Host"] ?? "localhost";
    var factory = new RabbitMQ.Client.ConnectionFactory { HostName = rmqHost };
    using var conn = factory.CreateConnection();
    using var ch = conn.CreateModel();
    RabbitMqTopology.DeclareConsumerQueue(ch, "recurring.failed-rule", "recurring.failed");
}
catch (Exception)
{
    // Bỏ qua nếu RabbitMQ chưa chạy
}

// Cấu hình JWT Bearer chung từ SharedKernel
builder.Services.AddJwtAuthentication(builder.Configuration);

// Cấu hình Consul Service Discovery từ SharedKernel
builder.Services.AddConsulConfig(builder.Configuration);

// Chuẩn hóa Health Checks & Prometheus Metrics (D61)
builder.Services.AddServiceHealthChecks<RecurringDebtDbContext>(builder.Configuration, checkRabbitMq: true);

var app = builder.Build();

// Khởi tạo Database nếu cần
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<RecurringDebtDbContext>();
    var conn = db.Database.GetDbConnection();
    if (!string.IsNullOrEmpty(conn.DataSource) && Path.GetDirectoryName(conn.DataSource) is { Length: > 0 } dir)
    {
        Directory.CreateDirectory(dir);
    }
    db.Database.Migrate();
    db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();
app.UseAuthorization();

// Endpoint kiểm tra sức khỏe & metrics (D61)
app.UseServiceHealthAndMetrics("RecurringDebtService");

// Đăng ký dịch vụ với Consul
app.UseConsulServiceRegistration(builder.Configuration, app.Lifetime);

app.MapControllers();

app.Run();
