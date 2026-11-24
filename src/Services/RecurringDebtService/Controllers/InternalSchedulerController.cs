using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RecurringDebtService.Services;

namespace RecurringDebtService.Controllers;

[ApiController]
[Route("internal/scheduler")]
[AllowAnonymous] // D54: endpoint nội bộ cho QA/test runner gọi trực tiếp qua port 8008
public class InternalSchedulerController : ControllerBase
{
    private readonly ISchedulerEngine _schedulerEngine;
    private readonly IConfiguration _configuration;
    private readonly ILogger<InternalSchedulerController> _logger;

    public InternalSchedulerController(
        ISchedulerEngine schedulerEngine,
        IConfiguration configuration,
        ILogger<InternalSchedulerController> logger)
    {
        _schedulerEngine = schedulerEngine;
        _configuration = configuration;
        _logger = logger;
    }

    [HttpPost("run")]
    public async Task<IActionResult> RunScheduler([FromQuery] string? asOf, CancellationToken cancellationToken)
    {
        // D54: Chỉ kích hoạt khi cấu hình Scheduler:EnableManualTrigger = true
        var enabled = _configuration.GetValue<bool>("Scheduler:EnableManualTrigger", false);
        if (!enabled)
        {
            _logger.LogWarning("Yêu cầu kích hoạt thủ công Scheduler bị từ chối do cờ EnableManualTrigger tắt.");
            return NotFound(new { message = "Manual trigger disabled" });
        }

        DateOnly asOfDate;
        if (!string.IsNullOrWhiteSpace(asOf) && DateOnly.TryParse(asOf, out var parsedDate))
        {
            asOfDate = parsedDate;
        }
        else
        {
            asOfDate = _schedulerEngine.GetTodayVietnam();
        }

        _logger.LogInformation("Nhận yêu cầu chạy Scheduler nội bộ cho ngày asOf: {AsOfDate}", asOfDate);
        await _schedulerEngine.RunSweepAsync(asOfDate, cancellationToken);

        return Ok(new
        {
            message = $"Scheduler sweep completed successfully for {asOfDate:yyyy-MM-dd}",
            asOf = asOfDate.ToString("yyyy-MM-dd")
        });
    }
}

