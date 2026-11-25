using IdentityService.Data;
using IdentityService.Services;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Extensions;
using SharedKernel.Messaging;

var builder = WebApplication.CreateBuilder(args);

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

// Đăng ký Services nghiệp vụ Identity
builder.Services.AddScoped<IPasswordHasher, PasswordHasher>();
builder.Services.AddScoped<ITokenService, TokenService>();
var emailChannel = System.Threading.Channels.Channel.CreateUnbounded<EmailMessage>();
builder.Services.AddSingleton(emailChannel);
builder.Services.AddSingleton<IEmailSender, LoggingEmailSender>();
builder.Services.AddHostedService<EmailDispatchBackgroundService>();
builder.Services.AddSingleton<IEventPublisher, RabbitMqEventPublisher>();

// Cấu hình JWT Bearer chung từ SharedKernel
builder.Services.AddJwtAuthentication(builder.Configuration);

// Cấu hình Consul Service Discovery từ SharedKernel
builder.Services.AddConsulConfig(builder.Configuration);

// Chuẩn hóa Health Checks & Prometheus Metrics (D61)
builder.Services.AddServiceHealthChecks<IdentityDbContext>(builder.Configuration, checkRabbitMq: true);

var app = builder.Build();

// Khởi tạo Database và Seed Roles
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
    await DbInitializer.InitializeAsync(context, app.Logger);
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

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
