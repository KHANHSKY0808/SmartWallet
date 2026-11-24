using Microsoft.EntityFrameworkCore;
using SharedKernel.Extensions;
using SharedKernel.Messaging;
using TransactionService.Consumers;
using TransactionService.Data;
using TransactionService.Services;

var builder = WebApplication.CreateBuilder(args);

// Cấu hình Controllers
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// Cấu hình Swagger
builder.Services.AddSwaggerGen();

// Cấu hình Database SQLite
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
    ?? "Data Source=../../../database/transaction.db;Cache=Shared";
builder.Services.AddDbContext<TransactionDbContext>(options =>
{
    options.UseSqlite(connectionString);
});

// Cấu hình HTTP Clients gọi WalletService và CategoryService với Polly timeout 3s (D1, D8)
builder.Services.AddHttpClient<IWalletClient, WalletClient>(client =>
{
    var url = builder.Configuration["Services:WalletService:Url"] ?? "http://localhost:8003";
    client.BaseAddress = new Uri(url);
    client.Timeout = TimeSpan.FromSeconds(3);
});

builder.Services.AddHttpClient<ICategoryClient, CategoryClient>(client =>
{
    var url = builder.Configuration["Services:CategoryService:Url"] ?? "http://localhost:8005";
    client.BaseAddress = new Uri(url);
    client.Timeout = TimeSpan.FromSeconds(3);
});

builder.Services.AddHttpClient<IFileServiceClient, FileServiceClient>(client =>
{
    var url = builder.Configuration["Services:FileService:Url"] ?? "http://localhost:8012";
    client.BaseAddress = new Uri(url);
    client.Timeout = TimeSpan.FromSeconds(3);
});

// RabbitMQ Event Publisher
builder.Services.AddSingleton<IEventPublisher, RabbitMqEventPublisher>();

// Đăng ký RabbitMQ Consumers
builder.Services.AddHostedService<MoneyTransferredConsumer>();
builder.Services.AddHostedService<GoalFundsDepositedConsumer>();
builder.Services.AddHostedService<GoalFundsWithdrawnConsumer>();

// Khai báo topology queue transaction.money-transferred và các queue goal
try
{
    var rmqHost = builder.Configuration["RabbitMQ:Host"] ?? "localhost";
    var factory = new RabbitMQ.Client.ConnectionFactory { HostName = rmqHost };
    using var conn = factory.CreateConnection();
    using var ch = conn.CreateModel();
    RabbitMqTopology.DeclareConsumerQueue(ch, "transaction.money-transferred", "wallet.transferred");
    RabbitMqTopology.DeclareConsumerQueue(ch, "transaction.goal-deposited", "goal.funds_deposited");
    RabbitMqTopology.DeclareConsumerQueue(ch, "transaction.goal-withdrawn", "goal.funds_withdrawn");
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

// Khởi tạo Database và thực hiện Migrate
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<TransactionDbContext>();
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
    service = "TransactionService",
    port = 8004,
    timestamp = DateTime.UtcNow
}));

// Đăng ký dịch vụ với Consul
app.UseConsulServiceRegistration(builder.Configuration, app.Lifetime);

app.MapControllers();

app.Run();

public partial class Program { }
