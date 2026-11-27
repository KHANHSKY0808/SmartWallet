using IdentityService.Data;
using IdentityService.Services;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Extensions;
using SharedKernel.Messaging;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.AddSmartWalletLogging("IdentityService");

// Cấu hình Controllers
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// Cấu hình Swagger
builder.Services.AddSwaggerGen();

// Cấu hình Database SQLite
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Data Source=../../../database/identity.db;Cache=Shared";
builder.Services.AddDbContext<IdentityDbContext>(options =>
{
    options.UseSqlite(connectionString);
});

// Cấu hình Redis cho D64/D65 (thu hồi phiên tức thì)
var redisConnStr = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379";
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
{
    var opts = ConfigurationOptions.Parse(redisConnStr);
    opts.AbortOnConnectFail = false;
    opts.ConnectTimeout = 500;
    opts.SyncTimeout = 500;
    opts.AsyncTimeout = 500;
    return ConnectionMultiplexer.Connect(opts);
});
builder.Services.AddSingleton<ISessionRevocationStore, RedisSessionRevocationStore>();
builder.Services.AddSingleton<IResendConfirmationRateLimiter, InMemoryResendConfirmationRateLimiter>();
builder.Services.AddSingleton(TimeProvider.System);

// Đăng ký Services nghiệp vụ Identity (D62: bỏ LoggingEmailSender & Channel, phát EmailRequestedEvent qua RabbitMQ)
builder.Services.AddScoped<IPasswordHasher, PasswordHasher>();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddSmartWalletOutbox<IdentityDbContext>();

// Cấu hình JWT Bearer & RBAC chung từ SharedKernel
builder.Services.AddJwtAuthentication(builder.Configuration);

// Cấu hình Consul Service Discovery từ SharedKernel
builder.Services.AddConsulConfig(builder.Configuration);

// Chuẩn hóa Health Checks & Prometheus Metrics (D61)
builder.Services.AddServiceHealthChecks<IdentityDbContext>(builder.Configuration, checkRabbitMq: true, checkRedis: true);

var app = builder.Build();

// Khởi tạo Database, Seed 3 Roles, 7 Permissions và Admin đầu tiên (D65, D66)
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
    var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
    await DbInitializer.InitializeAsync(context, app.Logger, builder.Configuration, hasher);
}

app.UseSmartWalletOpenApi();

app.UseSmartWalletRequestLogging();

app.UseAuthentication();
app.UseAuthorization();

// Endpoint kiểm tra sức khỏe & metrics (D61)
app.UseServiceHealthAndMetrics("IdentityService");

// Đăng ký dịch vụ với Consul
app.UseConsulServiceRegistration(builder.Configuration, app.Lifetime);

app.MapControllers();

app.Run();

// Hỗ trợ WebApplicationFactory trong Unit Test
public partial class Program { }
