using SharedKernel.Extensions;

var builder = WebApplication.CreateBuilder(args);

// 1. Cấu hình YARP Reverse Proxy
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

// 2. Cấu hình CORS cho Frontend Angular
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins("http://localhost:4200")
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});

// 3. JWT validation sơ bộ tại Gateway (nếu cần bảo vệ route tập trung)
builder.Services.AddJwtAuthentication(builder.Configuration);

var app = builder.Build();

// Middleware: X-Correlation-ID tracing
app.Use(async (context, next) =>
{
    const string correlationHeader = "X-Correlation-ID";
    if (!context.Request.Headers.TryGetValue(correlationHeader, out var correlationId) ||
        string.IsNullOrWhiteSpace(correlationId))
    {
        correlationId = Guid.NewGuid().ToString("N");
        context.Request.Headers[correlationHeader] = correlationId;
    }

    context.Response.Headers[correlationHeader] = correlationId;
    await next();
});

app.UseCors("AllowFrontend");

app.UseAuthentication();
app.UseAuthorization();

// Health check endpoint tại Gateway
app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    gateway = "ApiGateway.Yarp",
    port = 8000,
    timestamp = DateTime.UtcNow
}));

// YARP Proxy routing
app.MapReverseProxy();

app.Run();
