using Microsoft.EntityFrameworkCore;
using SharedKernel.Extensions;
using SharedKernel.Messaging;
using NotificationService.Consumers;
using NotificationService.Data;
using NotificationService.Services;

var builder = WebApplication.CreateBuilder(args);

builder.AddSmartWalletLogging("NotificationService");

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

// D62: Email Queue & SMTP Dispatcher
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ISmtpEmailClient, MailKitSmtpEmailClient>();
builder.Services.AddSingleton<IEmailDispatchSignal, EmailDispatchSignal>();
builder.Services.AddScoped<EmailDispatcherService>();
builder.Services.AddHostedService<EmailDispatchWorker>();

// Đăng ký Consumers
builder.Services.AddHostedService<BudgetThresholdReachedConsumer>();
builder.Services.AddHostedService<GoalCompletedConsumer>();
builder.Services.AddHostedService<DueReminderConsumer>();
builder.Services.AddHostedService<RecurringTransactionFailedConsumer>();
builder.Services.AddHostedService<ExportCompletedConsumer>();
builder.Services.AddHostedService<MoneyTransferredConsumer>();
builder.Services.AddHostedService<EmailRequestedConsumer>();

// Khai báo topology queue thông báo
try
{
    var rmqHost = builder.Configuration["RabbitMQ:Host"] ?? "localhost";
    var factory = new RabbitMQ.Client.ConnectionFactory { HostName = rmqHost };
    using var conn = factory.CreateConnection();
    using var ch = conn.CreateModel();
    RabbitMqTopology.DeclareConsumerQueue(ch, "notification.budget-threshold", "budget.threshold_reached");
    RabbitMqTopology.DeclareConsumerQueue(ch, "notification.goal-completed", "goal.completed");
    RabbitMqTopology.DeclareConsumerQueue(ch, "notification.due-reminder", "reminder.due");
    RabbitMqTopology.DeclareConsumerQueue(ch, "notification.recurring-failed", "recurring.failed");
    RabbitMqTopology.DeclareConsumerQueue(ch, "notification.export-completed", "report.export_completed");
    RabbitMqTopology.DeclareConsumerQueue(ch, "notification.money-transferred", "wallet.transferred");
    RabbitMqTopology.DeclareConsumerQueue(ch, "notification.email-requested", "identity.email_requested");
}
catch (Exception)
{
    // Bỏ qua nếu RabbitMQ chưa sẵn sàng ở môi trường test không có broker
}

// Cấu hình JWT Bearer chung từ SharedKernel
builder.Services.AddJwtAuthentication(builder.Configuration);

// Cấu hình Consul Service Discovery từ SharedKernel
builder.Services.AddConsulConfig(builder.Configuration);

// Chuẩn hóa Health Checks & Prometheus Metrics (D61)
builder.Services.AddServiceHealthChecks<NotificationDbContext>(builder.Configuration, checkRabbitMq: true);

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

app.UseSmartWalletOpenApi();

app.UseSmartWalletRequestLogging();

app.UseAuthentication();
app.UseAuthorization();

// Endpoint kiểm tra sức khỏe & metrics (D61)
app.UseServiceHealthAndMetrics("NotificationService");

// Đăng ký dịch vụ với Consul
app.UseConsulServiceRegistration(builder.Configuration, app.Lifetime);

app.MapControllers();

app.Run();
