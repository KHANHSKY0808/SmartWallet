using Microsoft.EntityFrameworkCore;
using SharedKernel.Extensions;
using SharedKernel.Messaging;
using CategoryService.Data;
using CategoryService.Services;

var builder = WebApplication.CreateBuilder(args);

// Cấu hình Controllers
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// Cấu hình Swagger
builder.Services.AddSwaggerGen();

// Cấu hình Database SQLite
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
    ?? "Data Source=../../../database/category.db;Cache=Shared";
builder.Services.AddDbContext<CategoryDbContext>(options =>
{
    options.UseSqlite(connectionString);
});

// Cấu hình HTTP Client gọi TransactionService (AC9: Reassign category)
builder.Services.AddHttpClient<ITransactionClient, TransactionClient>(client =>
{
    var url = builder.Configuration["Services:TransactionService:Url"] ?? "http://localhost:8004";
    client.BaseAddress = new Uri(url);
    client.Timeout = TimeSpan.FromSeconds(3);
});

builder.Services.AddSingleton<IEventPublisher, RabbitMqEventPublisher>();

// Cấu hình JWT Bearer chung từ SharedKernel
builder.Services.AddJwtAuthentication(builder.Configuration);

// Cấu hình Consul Service Discovery từ SharedKernel
builder.Services.AddConsulConfig(builder.Configuration);

// Chuẩn hóa Health Checks & Prometheus Metrics (D61)
builder.Services.AddServiceHealthChecks<CategoryDbContext>(builder.Configuration, checkRabbitMq: true);

var app = builder.Build();

// Khởi tạo Database và Seed 20 danh mục mặc định
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<CategoryDbContext>();
    var conn = context.Database.GetDbConnection();
    if (!string.IsNullOrEmpty(conn.DataSource) && Path.GetDirectoryName(conn.DataSource) is { Length: > 0 } dir)
    {
        Directory.CreateDirectory(dir);
    }
    context.Database.Migrate();
    await DefaultCategorySeeder.SeedAsync(context, app.Logger);
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();
app.UseAuthorization();

// Endpoint kiểm tra sức khỏe & metrics (D61)
app.UseServiceHealthAndMetrics("CategoryService");

// Đăng ký dịch vụ với Consul
app.UseConsulServiceRegistration(builder.Configuration, app.Lifetime);

app.MapControllers();

app.Run();

public partial class Program { }
