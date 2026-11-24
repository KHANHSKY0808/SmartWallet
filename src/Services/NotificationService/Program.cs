using Microsoft.EntityFrameworkCore;
using SharedKernel.Extensions;
using SharedKernel.Messaging;
using NotificationService.Consumers;
using NotificationService.Data;

var builder = WebApplication.CreateBuilder(args);

// Cấu hình Controllers
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// Cấu hình Swagger
builder.Services.AddSwaggerGen();

// Cấu hình Database SQLite
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
    ?? "Data Source=../../../database/notification.db;Cache=Shared";
builder.Services.AddDbContext<NotificationDbContext>(options =>
{
    options.UseSqlite(connectionString);
});

// Đăng ký Consumer thông báo ngân sách
builder.Services.AddHostedService<BudgetThresholdReachedConsumer>();

// Khai báo topology queue thông báo
try
{
    var rmqHost = builder.Configuration["RabbitMQ:Host"] ?? "localhost";
    var factory = new RabbitMQ.Client.ConnectionFactory { HostName = rmqHost };
    using var conn = factory.CreateConnection();
    using var ch = conn.CreateModel();
    RabbitMqTopology.DeclareConsumerQueue(ch, "notification.budget-threshold", "budget.threshold_reached");
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

// Khởi tạo Database nếu cần & áp dụng Migrations
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
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
    service = "NotificationService",
    port = 8010,
    timestamp = DateTime.UtcNow
}));

// Đăng ký dịch vụ với Consul
app.UseConsulServiceRegistration(builder.Configuration, app.Lifetime);

app.MapControllers();

app.Run();
