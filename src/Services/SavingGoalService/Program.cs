using Microsoft.EntityFrameworkCore;
using SharedKernel.Extensions;
using SharedKernel.Messaging;
using SavingGoalService.Consumers;
using SavingGoalService.Data;
using SavingGoalService.Services;

var builder = WebApplication.CreateBuilder(args);

builder.AddSmartWalletLogging("SavingGoalService");

// Cấu hình Controllers
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Cấu hình Database SQLite
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
    ?? "Data Source=../../../database/savinggoalservice.db;Cache=Shared";
builder.Services.AddDbContext<SavingGoalDbContext>(options =>
{
    options.UseSqlite(connectionString);
});

// Cấu hình WalletServiceClient
builder.Services.AddHttpClient<IWalletServiceClient, WalletServiceClient>(client =>
{
    var url = builder.Configuration["Services:WalletService:Url"] ?? "http://localhost:8003";
    client.BaseAddress = new Uri(url);
    client.Timeout = TimeSpan.FromSeconds(3);
}).AddHttpMessageHandler(sp => new SystemTokenDelegatingHandler(sp.GetRequiredService<IConfiguration>(), "SavingGoalService"));

// Cấu hình Event Publisher và Consumers
builder.Services.AddSmartWalletOutbox<SavingGoalDbContext>();
builder.Services.AddHostedService<GoalFundsDepositedConsumer>();

// Khai báo topology queue
try
{
    var rmqHost = builder.Configuration["RabbitMQ:Host"] ?? "localhost";
    var factory = new RabbitMQ.Client.ConnectionFactory { HostName = rmqHost };
    using var conn = factory.CreateConnection();
    using var ch = conn.CreateModel();
    RabbitMqTopology.DeclareConsumerQueue(ch, "savinggoal.funds-deposited", "goal.funds_deposited");
}
catch (Exception ex)
{
    Console.WriteLine($"[RabbitMQ Topology] Warning: Không thể khởi tạo queue: {ex.Message}");
}

// Cấu hình JWT Authentication
builder.Services.AddJwtAuthentication(builder.Configuration);

// Cấu hình Consul Discovery
builder.Services.AddConsulConfig(builder.Configuration);

// Chuẩn hóa Health Checks & Prometheus Metrics (D61)
builder.Services.AddServiceHealthChecks<SavingGoalDbContext>(builder.Configuration, checkRabbitMq: true);

var app = builder.Build();

// Khởi tạo Database và đặt SQLite WAL mode
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SavingGoalDbContext>();
    try
    {
        var conn = db.Database.GetDbConnection();
        if (!string.IsNullOrEmpty(conn.DataSource) && Path.GetDirectoryName(conn.DataSource) is { Length: > 0 } dir)
        {
            Directory.CreateDirectory(dir);
        }
        db.Database.Migrate();
        db.Database.ExecuteSqlRaw("PRAGMA journal_mode = WAL;");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[DB Init] Warning: {ex.Message}");
    }
}

app.UseSmartWalletOpenApi();

app.UseSmartWalletRequestLogging();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.UseServiceHealthAndMetrics("SavingGoalService");

// Tự động đăng ký Consul khi khởi động
app.UseConsulServiceRegistration(builder.Configuration, app.Lifetime);

app.Run();
