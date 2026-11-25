using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ReportService.Data;
using ReportService.DTOs;
using ReportService.Models;
using ReportService.Services;

namespace ReportService.Controllers;

[ApiController]
[Route("api/reports")]
[Authorize]
public class ReportsController : ControllerBase
{
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> UserExportLocks = new();
    private static readonly HashSet<string> AllowedGroupBy = new(StringComparer.OrdinalIgnoreCase)
    {
        "day", "week", "month", "year"
    };

    private readonly ReportDbContext _db;
    private readonly IReportQueryService _queryService;
    private readonly IConfiguration _configuration;
    private readonly TimeProvider _timeProvider;

    public ReportsController(
        ReportDbContext db,
        IReportQueryService queryService,
        IConfiguration configuration,
        TimeProvider? timeProvider = null)
    {
        _db = db;
        _queryService = queryService;
        _configuration = configuration;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    [HttpGet("dashboard")]
    public async Task<IActionResult> GetDashboard([FromQuery] int? month, [FromQuery] int? year, CancellationToken cancellationToken)
    {
        var (defaultMonth, defaultYear) = VietnamTimeHelper.GetCurrentMonthYearInVietnam(_timeProvider);
        int targetMonth = month ?? defaultMonth;
        int targetYear = year ?? defaultYear;

        if (targetMonth < 1 || targetMonth > 12)
        {
            return BadRequest(new { message = "Tháng không hợp lệ (1–12)." });
        }

        if (targetYear < 2000 || targetYear > 2100)
        {
            return BadRequest(new { message = "Năm không hợp lệ (2000–2100)." });
        }

        var userId = GetCurrentUserId();
        var token = Request.Headers.Authorization.FirstOrDefault();
        var result = await _queryService.GetDashboardAsync(userId, targetMonth, targetYear, token, cancellationToken);
        return Ok(result);
    }

    [HttpGet("analysis")]
    public async Task<IActionResult> GetAnalysis(
        [FromQuery] string? from,
        [FromQuery] string? to,
        [FromQuery] string groupBy = "month",
        [FromQuery] string? compare = null,
        CancellationToken cancellationToken = default)
    {
        if (!DateOnly.TryParse(from, out var fromDate) || !DateOnly.TryParse(to, out var toDate))
        {
            return BadRequest(new { message = "Tham số ngày 'from' và 'to' phải đúng định dạng YYYY-MM-DD." });
        }

        if (toDate < fromDate)
        {
            return BadRequest(new { message = "Ngày kết thúc không được nhỏ hơn ngày bắt đầu." });
        }

        if (string.IsNullOrWhiteSpace(groupBy) || !AllowedGroupBy.Contains(groupBy.Trim()))
        {
            return BadRequest(new { message = "Tham số groupBy chỉ hỗ trợ day, week, month, year." });
        }

        if (toDate > fromDate.AddYears(2))
        {
            return BadRequest(new { message = "Khoảng thời gian phân tích tối đa là 2 năm." });
        }

        var normGroup = groupBy.Trim().ToLowerInvariant();
        if (normGroup == "day" && (toDate.DayNumber - fromDate.DayNumber + 1) > 366)
        {
            return BadRequest(new { message = "Phân tích theo ngày tối đa 366 ngày." });
        }

        bool comparePrevious = string.Equals(compare?.Trim(), "previous", StringComparison.OrdinalIgnoreCase);
        var userId = GetCurrentUserId();
        var result = await _queryService.GetAnalysisAsync(userId, fromDate, toDate, normGroup, comparePrevious, cancellationToken);
        return Ok(result);
    }

    [HttpPost("exports")]
    public async Task<IActionResult> CreateExport([FromBody] CreateExportRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var fmtNorm = request.Format?.Trim().ToLowerInvariant();
        if (fmtNorm != "xlsx" && fmtNorm != "pdf")
        {
            return BadRequest(new { message = "Định dạng chỉ hỗ trợ xlsx hoặc pdf." });
        }

        if (!DateOnly.TryParse(request.From, out var fromDate) || !DateOnly.TryParse(request.To, out var toDate))
        {
            return BadRequest(new { message = "Ngày 'from' và 'to' phải đúng định dạng YYYY-MM-DD." });
        }

        if (toDate < fromDate)
        {
            return BadRequest(new { message = "Ngày kết thúc không được nhỏ hơn ngày bắt đầu." });
        }

        if (toDate > fromDate.AddYears(2))
        {
            return BadRequest(new { message = "Khoảng thời gian xuất báo cáo tối đa là 2 năm." });
        }

        var userId = GetCurrentUserId();
        var userLock = UserExportLocks.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));
        await userLock.WaitAsync(cancellationToken);
        try
        {
            // D60 / EX-04: Tối đa 3 job Queued + Processing mỗi user
            var activeCount = await _db.ExportJobs.CountAsync(
                j => j.UserId == userId && (j.Status == ExportJobStatus.Queued || j.Status == ExportJobStatus.Processing),
                cancellationToken);

            if (activeCount >= 3)
            {
                return StatusCode(429, new
                {
                    code = "EXPORT_LIMIT",
                    message = "Bạn đã đạt giới hạn tối đa 3 tác vụ xuất báo cáo đang xử lý."
                });
            }

            var job = new ExportJob
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                ReportType = "Transactions",
                Format = fmtNorm == "pdf" ? ExportFormat.Pdf : ExportFormat.Xlsx,
                ParametersJson = JsonSerializer.Serialize(new ExportParameters
                {
                    From = fromDate.ToString("yyyy-MM-dd"),
                    To = toDate.ToString("yyyy-MM-dd")
                }),
                Status = ExportJobStatus.Queued,
                FileId = null,
                CreatedAt = _timeProvider.GetUtcNow().UtcDateTime
            };

            _db.ExportJobs.Add(job);
            await _db.SaveChangesAsync(cancellationToken);

            return StatusCode(202, ToExportDto(job, fromDate, toDate));
        }
        finally
        {
            userLock.Release();
        }
    }

    [HttpGet("exports")]
    public async Task<IActionResult> ListExports(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var jobs = await _db.ExportJobs
            .AsNoTracking()
            .Where(j => j.UserId == userId)
            .OrderByDescending(j => j.CreatedAt)
            .Take(50)
            .ToListAsync(cancellationToken);

        return Ok(jobs.Select(j => ToExportDto(j)).ToList());
    }

    [HttpGet("exports/{id:guid}")]
    public async Task<IActionResult> GetExport(Guid id, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var job = await _db.ExportJobs
            .AsNoTracking()
            .FirstOrDefaultAsync(j => j.Id == id && j.UserId == userId, cancellationToken);

        if (job == null)
        {
            return NotFound(new { message = "Tác vụ xuất báo cáo không tồn tại." });
        }

        return Ok(ToExportDto(job));
    }

    [HttpGet("exports/{id:guid}/file")]
    public async Task<IActionResult> DownloadExportFile(Guid id, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var job = await _db.ExportJobs
            .AsNoTracking()
            .FirstOrDefaultAsync(j => j.Id == id && j.UserId == userId, cancellationToken);

        if (job == null || job.Status != ExportJobStatus.Completed || string.IsNullOrWhiteSpace(job.StorageFileName))
        {
            return NotFound(new { message = "Tệp báo cáo không tồn tại hoặc chưa hoàn tất." });
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        if (job.ExpiresAt.HasValue && job.ExpiresAt.Value < nowUtc)
        {
            return NotFound(new { message = "Liên kết tải báo cáo đã hết hạn." });
        }

        var configured = _configuration["Export:StoragePath"];
        var dir = !string.IsNullOrWhiteSpace(configured)
            ? configured
            : Path.Combine(AppContext.BaseDirectory, "exports");

        var fullPath = Path.Combine(dir, job.StorageFileName);
        if (!System.IO.File.Exists(fullPath))
        {
            return NotFound(new { message = "Tệp báo cáo không còn tồn tại trên hệ thống." });
        }

        var bytes = await System.IO.File.ReadAllBytesAsync(fullPath, cancellationToken);
        var isPdf = job.Format == ExportFormat.Pdf;
        var contentType = isPdf
            ? "application/pdf"
            : "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
        var downloadName = isPdf
            ? $"smartwallet-report-{job.Id}.pdf"
            : $"smartwallet-report-{job.Id}.xlsx";

        return File(bytes, contentType, downloadName);
    }

    private Guid GetCurrentUserId()
    {
        var idStr = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(idStr, out var id) ? id : Guid.Empty;
    }

    private static ExportJobDto ToExportDto(ExportJob job, DateOnly? fromOverride = null, DateOnly? toOverride = null)
    {
        string fromStr = fromOverride?.ToString("yyyy-MM-dd") ?? string.Empty;
        string toStr = toOverride?.ToString("yyyy-MM-dd") ?? string.Empty;
        if (string.IsNullOrEmpty(fromStr) && !string.IsNullOrEmpty(job.ParametersJson))
        {
            try
            {
                var p = JsonSerializer.Deserialize<ExportParameters>(job.ParametersJson);
                if (p != null)
                {
                    fromStr = p.From;
                    toStr = p.To;
                }
            }
            catch { }
        }

        return new ExportJobDto
        {
            JobId = job.Id,
            Format = job.Format == ExportFormat.Pdf ? "pdf" : "xlsx",
            From = fromStr,
            To = toStr,
            Status = job.Status.ToString(),
            ErrorMessage = job.ErrorMessage,
            CreatedAt = job.CreatedAt,
            CompletedAt = job.CompletedAt,
            ExpiresAt = job.ExpiresAt
        };
    }
}

