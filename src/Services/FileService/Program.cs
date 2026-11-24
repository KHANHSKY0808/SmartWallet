using Microsoft.EntityFrameworkCore;
using SharedKernel.Extensions;
using FileService.Consumers;
using FileService.Data;
using FileService.Services;

var builder = WebApplication.CreateBuilder(args);

// Cấu hình Database
builder.Services.AddDbContext<FileDbContext>(options =>
{
    var conn = builder.Configuration.GetConnectionString("DefaultConnection") 
               ?? "Data Source=fileservice.db";
    options.UseSqlite(conn);
});

// Cấu hình Controllers
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// Cấu hình Swagger
builder.Services.AddSwaggerGen();

// Cấu hình JWT Bearer chung từ SharedKernel
builder.Services.AddJwtAuthentication(builder.Configuration);

// Cấu hình Consul Service Discovery từ SharedKernel
builder.Services.AddConsulConfig(builder.Configuration);

// Cấu hình RabbitMQ Consumer và Background Services
builder.Services.AddHostedService<TransactionDeletedConsumer>();
builder.Services.AddHostedService<OrphanFileCleanupService>();

var app = builder.Build();

// Tự động migrate DB
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<FileDbContext>();
    var conn = db.Database.GetDbConnection();
    if (!string.IsNullOrEmpty(conn.DataSource) && Path.GetDirectoryName(conn.DataSource) is { Length: > 0 } dir)
    {
        Directory.CreateDirectory(dir);
    }
    db.Database.Migrate();
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
    service = "FileService",
    port = 8012,
    timestamp = DateTime.UtcNow
}));

// Đăng ký dịch vụ với Consul
app.UseConsulServiceRegistration(builder.Configuration, app.Lifetime);

app.MapControllers();

app.Run();

public partial class Program { }
