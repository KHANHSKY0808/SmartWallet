using Microsoft.EntityFrameworkCore;
using SharedKernel.Extensions;
using SharedKernel.Messaging;
using SavingGoalService.Consumers;
using SavingGoalService.Data;

var builder = WebApplication.CreateBuilder(args);

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

// Cấu hình Event Publisher và Consumers
builder.Services.AddSingleton<IEventPublisher, RabbitMqEventPublisher>();
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

var app = builder.Build();

// Khởi tạo Database và đặt SQLite WAL mode
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SavingGoalDbContext>();
    try
    {
        db.Database.EnsureCreated();
        db.Database.ExecuteSqlRaw("PRAGMA journal_mode = WAL;");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[DB Init] Warning: {ex.Message}");
    }
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapGet("/health", () => Results.Ok(new { status = "Healthy", service = "SavingGoalService" }));

// Tự động đăng ký Consul khi khởi động
app.UseConsulServiceRegistration(builder.Configuration, app.Lifetime);

app.Run();
