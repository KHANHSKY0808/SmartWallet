using Microsoft.EntityFrameworkCore;
using SharedKernel.Extensions;
using SharedKernel.Messaging;
using WalletService.Consumers;
using WalletService.Data;
using WalletService.Services;

var builder = WebApplication.CreateBuilder(args);

// Cấu hình Controllers
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// Cấu hình Swagger
builder.Services.AddSwaggerGen();

// Cấu hình Database SQLite
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
    ?? "Data Source=../../../database/wallet.db;Cache=Shared";
builder.Services.AddDbContext<WalletDbContext>(options =>
{
    options.UseSqlite(connectionString);
});

// Đăng ký Ledger và HTTP Client gọi UserProfileService
builder.Services.AddScoped<IWalletLedger, WalletLedger>();
builder.Services.AddHttpClient<IUserProfileClient, UserProfileClient>(client =>
{
    var url = builder.Configuration["Services:UserProfileService:Url"] ?? "http://localhost:8002";
    client.BaseAddress = new Uri(url);
    client.Timeout = TimeSpan.FromSeconds(3); // D8: timeout 3s
});

builder.Services.AddSingleton<IEventPublisher, RabbitMqEventPublisher>();

// Đăng ký RabbitMQ Consumers cho các sự kiện giao dịch (FIN-36)
builder.Services.AddHostedService<TransactionCreatedConsumer>();
builder.Services.AddHostedService<TransactionUpdatedConsumer>();
builder.Services.AddHostedService<TransactionDeletedConsumer>();

// Khai báo topology queue transaction.money-transferred và các queue giao dịch
try
{
    var rmqHost = builder.Configuration["RabbitMQ:Host"] ?? "localhost";
    var factory = new RabbitMQ.Client.ConnectionFactory { HostName = rmqHost };
    using var conn = factory.CreateConnection();
    using var ch = conn.CreateModel();
    RabbitMqTopology.DeclareConsumerQueue(ch, "transaction.money-transferred", "wallet.transferred");
    RabbitMqTopology.DeclareConsumerQueue(ch, "wallet.transaction-created", "transaction.created");
    RabbitMqTopology.DeclareConsumerQueue(ch, "wallet.transaction-updated", "transaction.updated");
    RabbitMqTopology.DeclareConsumerQueue(ch, "wallet.transaction-deleted", "transaction.deleted");
}
catch (Exception)
{
    // Bỏ qua nếu RabbitMQ chưa sẵn sàng ở môi trường test không có broker
}

// Cấu hình JWT Bearer chung từ SharedKernel
builder.Services.AddJwtAuthentication(builder.Configuration);

// Cấu hình Consul Service Discovery từ SharedKernel
builder.Services.AddConsulConfig(builder.Configuration);

var app = builder.Build();

// Khởi tạo Database nếu cần
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<WalletDbContext>();
    var conn = context.Database.GetDbConnection();
    if (!string.IsNullOrEmpty(conn.DataSource) && Path.GetDirectoryName(conn.DataSource) is { Length: > 0 } dir)
    {
        Directory.CreateDirectory(dir);
    }
    context.Database.Migrate();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();
app.UseAuthorization();

// Endpoint kiểm tra sức khỏe dịch vụ
app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    service = "WalletService",
    port = 8003,
    timestamp = DateTime.UtcNow
}));

// Đăng ký dịch vụ với Consul
app.UseConsulServiceRegistration(builder.Configuration, app.Lifetime);

app.MapControllers();

app.Run();

public partial class Program { }
