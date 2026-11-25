using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using SharedKernel.Extensions;
using UserProfileService.Consumers;
using UserProfileService.Data;
using UserProfileService.Services;

var builder = WebApplication.CreateBuilder(args);

// Cấu hình Controllers
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// Cấu hình Swagger
builder.Services.AddSwaggerGen();

// Cấu hình Database SQLite
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
    ?? "Data Source=../../../database/profile.db;Cache=Shared";
builder.Services.AddDbContext<ProfileDbContext>(options =>
{
    options.UseSqlite(connectionString);
});

// Đăng ký Services nghiệp vụ
builder.Services.AddScoped<IProfileService, ProfileService>();

// Đăng ký RabbitMQ Consumer Hosted Service
builder.Services.AddHostedService<UserRegisteredConsumer>();

// Cấu hình JWT Bearer chung từ SharedKernel
builder.Services.AddJwtAuthentication(builder.Configuration);

// Cấu hình Consul Service Discovery từ SharedKernel
builder.Services.AddConsulConfig(builder.Configuration);

// Chuẩn hóa Health Checks & Prometheus Metrics (D61)
builder.Services.AddServiceHealthChecks<ProfileDbContext>(builder.Configuration, checkRabbitMq: true);

var app = builder.Build();

// Khởi tạo Database và Seed Currencies
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<ProfileDbContext>();
    await ProfileDbInitializer.InitializeAsync(context, app.Logger);
}

// Cấu hình Static Files cho uploads
var uploadsDir = Path.Combine(app.Environment.ContentRootPath, "uploads");
if (!Directory.Exists(uploadsDir))
{
    Directory.CreateDirectory(uploadsDir);
}

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(uploadsDir),
    RequestPath = "/uploads"
});

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();
app.UseAuthorization();

// Endpoint kiểm tra sức khỏe & metrics (D61)
app.UseServiceHealthAndMetrics("UserProfileService");

// Đăng ký dịch vụ với Consul
app.UseConsulServiceRegistration(builder.Configuration, app.Lifetime);

app.MapControllers();

app.Run();

// Hỗ trợ WebApplicationFactory trong Unit Test
public partial class Program { }
