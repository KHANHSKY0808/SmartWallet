namespace RecurringDebtService.Services;

public class RecurringSchedulerBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<RecurringSchedulerBackgroundService> _logger;

    public RecurringSchedulerBackgroundService(
        IServiceProvider serviceProvider,
        ILogger<RecurringSchedulerBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("RecurringSchedulerBackgroundService started.");

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(30));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var engine = scope.ServiceProvider.GetRequiredService<ISchedulerEngine>();
                var today = engine.GetTodayVietnam();
                await engine.RunSweepAsync(today, stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Lỗi xảy ra trong vòng quét định kỳ của Scheduler");
            }

            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}

