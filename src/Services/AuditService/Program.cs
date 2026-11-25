using AuditService.Consumers;
using AuditService.Data;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Extensions;
using SharedKernel.Messaging;

var builder = WebApplication.CreateBuilder(args);

// Cấu hình Controllers
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// Cấu hình Swagger
builder.Services.AddSwaggerGen();

// Cấu hình Database SQLite (D48/D63)
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Data Source=../../../database/audit.db;Cache=Shared";
builder.Services.AddDbContext<AuditDbContext>(options =>
{
    options.UseSqlite(connectionString);
});

// Consumer Audit nhận toàn bộ domain events (#)
builder.Services.AddHostedService<AuditAllConsumer>();

try
{
    var rmqHost = builder.Configuration["RabbitMQ:Host"] ?? "localhost";
    var factory = new RabbitMQ.Client.ConnectionFactory { HostName = rmqHost };
    using var conn = factory.CreateConnection();
    using var ch = conn.CreateModel();
    RabbitMqTopology.DeclareConsumerQueue(ch, "audit.all", "#");
}
catch (Exception)
{
    // RabbitMQ chưa chạy -> AuditAllConsumer sẽ tự thử lại
}

// Cấu hình JWT Bearer & RBAC chung từ SharedKernel
builder.Services.AddJwtAuthentication(builder.Configuration);

// Cấu hình Consul Service Discovery từ SharedKernel
builder.Services.AddConsulConfig(builder.Configuration);

// Chuẩn hóa Health Checks & Prometheus Metrics (D61)
builder.Services.AddServiceHealthChecks<AuditDbContext>(builder.Configuration, checkRabbitMq: true);

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
    var conn = context.Database.GetDbConnection();
    if (!string.IsNullOrEmpty(conn.DataSource) && Path.GetDirectoryName(conn.DataSource) is { Length: > 0 } dir)
    {
        Directory.CreateDirectory(dir);
    }
    await context.Database.MigrateAsync();
    try
    {
        await context.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
    }
    catch { }
    await AuditDbContext.EnsureImmutableTriggersAsync(context);
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();
app.UseAuthorization();

// Endpoint kiểm tra sức khỏe & metrics (D61)
app.UseServiceHealthAndMetrics("AuditService");

// Đăng ký dịch vụ với Consul
app.UseConsulServiceRegistration(builder.Configuration, app.Lifetime);

app.MapControllers();

app.Run();

public partial class Program { }
