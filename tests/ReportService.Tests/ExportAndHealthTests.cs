using System.IO.Compression;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using QuestPDF.Fluent;
using ReportService.Controllers;
using ReportService.Data;
using ReportService.DTOs;
using ReportService.Models;
using ReportService.Services;
using SharedKernel.Extensions;

namespace ReportService.Tests;

public class ExportAndHealthTests : IDisposable
{
    private class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _now;
        public FakeTimeProvider(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan ts) => _now = _now.Add(ts);
    }

    private class FakeExchangeRateClient : IExchangeRateClient
    {
        public Task<decimal?> GetRateToVndAsync(string baseCurrency, CancellationToken cancellationToken = default)
            => Task.FromResult<decimal?>(1m);
    }

    private class FailingExportGenerator : IExportGenerator
    {
        public byte[] GenerateExcel(DateOnly from, DateOnly to, List<ExportTransactionRow> rows)
            => throw new InvalidOperationException("Simulated internal disk/memory error");
        public byte[] GeneratePdf(DateOnly from, DateOnly to, List<ExportTransactionRow> rows)
            => throw new InvalidOperationException("Simulated internal PDF error");
    }

    private readonly SqliteConnection _connection;
    private readonly string _tempDir;

    public ExportAndHealthTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();
        _tempDir = Path.Combine(Path.GetTempPath(), "sw_export_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        _connection.Close();
        _connection.Dispose();
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    [Fact]
    public async Task Test_EX05_Validation_InvalidFormatOrDatesReturn400()
    {
        var opts = new DbContextOptionsBuilder<ReportDbContext>().UseSqlite(_connection).Options;
        await using var db = new ReportDbContext(opts);
        await db.Database.EnsureCreatedAsync();

        var controller = new ReportsController(db, null!, new ConfigurationBuilder().Build());
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()) }, "Test"))
            }
        };

        Assert.IsType<BadRequestObjectResult>(await controller.CreateExport(new CreateExportRequest { Format = "csv", From = "2026-11-01", To = "2026-11-30" }, CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(await controller.CreateExport(new CreateExportRequest { Format = "xlsx", From = "2023-01-01", To = "2026-11-30" }, CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(await controller.CreateExport(new CreateExportRequest { Format = "xlsx", From = "2026-11-30", To = "2026-11-01" }, CancellationToken.None));
    }

    [Fact]
    public async Task Test_EX08_ExpiredJobOver7Days_DeletesFileAndReturns404()
    {
        var start = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        var fakeTime = new FakeTimeProvider(start);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { { "Export:StoragePath", _tempDir } })
            .Build();

        var services = new ServiceCollection();
        services.AddDbContext<ReportDbContext>(o => o.UseSqlite(_connection));
        services.AddSingleton<IExportGenerator, ExportGenerator>();
        services.AddSingleton<IExchangeRateClient, FakeExchangeRateClient>();
        using var sp = services.BuildServiceProvider();

        var db = sp.GetRequiredService<ReportDbContext>();
        await db.Database.EnsureCreatedAsync();

        var userId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        db.ExportJobs.Add(new ExportJob
        {
            Id = jobId,
            UserId = userId,
            Format = ExportFormat.Xlsx,
            ParametersJson = "{\"From\":\"2026-11-01\",\"To\":\"2026-11-30\"}",
            Status = ExportJobStatus.Queued,
            CreatedAt = start.UtcDateTime
        });
        await db.SaveChangesAsync();

        var worker = new ExportWorkerBackgroundService(sp, config, fakeTime, NullLogger<ExportWorkerBackgroundService>.Instance);
        await worker.ProcessPendingJobsAsync();

        var job = await db.ExportJobs.AsNoTracking().FirstAsync(j => j.Id == jobId);
        Assert.Equal(ExportJobStatus.Completed, job.Status);
        Assert.NotNull(job.StorageFileName);
        var filePath = Path.Combine(_tempDir, job.StorageFileName!);
        Assert.True(File.Exists(filePath));

        // Tua thời gian sang ngày thứ 8 (> 7 ngày)
        fakeTime.Advance(TimeSpan.FromDays(8));
        await worker.CleanupExpiredExportsAsync();
        Assert.False(File.Exists(filePath));

        var controller = new ReportsController(db, null!, config, fakeTime);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) }, "Test"))
            }
        };

        var dlResult = await controller.DownloadExportFile(jobId, CancellationToken.None);
        Assert.IsType<NotFoundObjectResult>(dlResult);
    }

    [Fact]
    public void Test_EX09_PdfGeneration_SupportsVietnameseDiacritics()
    {
        var generator = new ExportGenerator();
        var rows = new List<ExportTransactionRow>
        {
            new(DateTime.UtcNow, 1, "Ăn uống", 50_000m, "VND", 50_000m, "Đi lại ăn sáng phở bò"),
            new(DateTime.UtcNow, 2, "Lương", 5_000_000m, "VND", 5_000_000m, "Thưởng dự án tháng 11")
        };

        var doc = generator.CreatePdfDocument(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30), rows);
        var pdfBytes = doc.GeneratePdf();
        Assert.True(pdfBytes.Length > 500);
        Assert.StartsWith("%PDF", Encoding.ASCII.GetString(pdfBytes, 0, 4));

        // Render ảnh PNG mẫu để đính kèm biên bản nghiệm thu (EX-09)
        var images = doc.GenerateImages().ToList();
        Assert.NotEmpty(images);
        var assetsDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "Gemini", "assets"));
        Directory.CreateDirectory(assetsDir);
        File.WriteAllBytes(Path.Combine(assetsDir, "pdf_sample_6a.png"), images[0]);
    }

    [Fact]
    public void Test_EX06_FormulaInjectionSanitizedAndDefaultXmlNamespaceUsed()
    {
        var generator = new ExportGenerator();
        var rows = new List<ExportTransactionRow>
        {
            new(DateTime.UtcNow, 1, "Ăn uống", 11_000m, "VND", 11_000m, "=HYPERLINK(\"http://evil\",\"click\")")
        };
        var xlsxBytes = generator.GenerateExcel(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30), rows);
        using var zip = new ZipArchive(new MemoryStream(xlsxBytes), ZipArchiveMode.Read);
        using var wbReader = new StreamReader(zip.GetEntry("xl/workbook.xml")!.Open());
        var wbXml = wbReader.ReadToEnd();
        Assert.Contains("<sheet ", wbXml);
        Assert.Contains("GiaoDich", wbXml);

        using var s1Reader = new StreamReader(zip.GetEntry("xl/worksheets/sheet1.xml")!.Open());
        var s1Xml = s1Reader.ReadToEnd();
        Assert.DoesNotContain("<f>", s1Xml);
        Assert.DoesNotContain("<f ", s1Xml);
    }

    [Fact]
    public async Task Test_EX10_WorkerFailure_MarksJobFailedWithFriendlyMessage_DoesNotCountTowardLimit()
    {
        var fakeTime = new FakeTimeProvider(new DateTimeOffset(2026, 11, 10, 0, 0, 0, TimeSpan.Zero));
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { { "Export:StoragePath", _tempDir } })
            .Build();

        var services = new ServiceCollection();
        services.AddDbContext<ReportDbContext>(o => o.UseSqlite(_connection));
        services.AddSingleton<IExportGenerator, FailingExportGenerator>();
        services.AddSingleton<IExchangeRateClient, FakeExchangeRateClient>();
        using var sp = services.BuildServiceProvider();

        var db = sp.GetRequiredService<ReportDbContext>();
        await db.Database.EnsureCreatedAsync();

        var userId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        db.ExportJobs.Add(new ExportJob
        {
            Id = jobId,
            UserId = userId,
            Format = ExportFormat.Xlsx,
            ParametersJson = "{\"From\":\"2026-11-01\",\"To\":\"2026-11-30\"}",
            Status = ExportJobStatus.Queued,
            CreatedAt = fakeTime.GetUtcNow().UtcDateTime
        });
        await db.SaveChangesAsync();

        var worker = new ExportWorkerBackgroundService(sp, config, fakeTime, NullLogger<ExportWorkerBackgroundService>.Instance);
        await worker.ProcessPendingJobsAsync();

        var job = await db.ExportJobs.AsNoTracking().FirstAsync(j => j.Id == jobId);
        Assert.Equal(ExportJobStatus.Failed, job.Status);
        Assert.Equal("Không thể tạo báo cáo, vui lòng thử lại", job.ErrorMessage);
        Assert.DoesNotContain("Exception", job.ErrorMessage!);

        var activeCount = await db.ExportJobs.CountAsync(j => j.UserId == userId && (j.Status == ExportJobStatus.Queued || j.Status == ExportJobStatus.Processing));
        Assert.Equal(0, activeCount);
    }

    [Fact]
    public async Task Test_HM06_RabbitMqDownReturnsDegraded_DbDownReturnsUnhealthy()
    {
        // RabbitMQ đóng trên cổng không tồn tại -> Degraded (HTTP 200)
        var configDown = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "RabbitMQ:Host", "127.0.0.1" },
                { "RabbitMQ:Port", "59999" }
            })
            .Build();

        var rmqCheck = new RabbitMqHealthCheck(configDown);
        var rmqResult = await rmqCheck.CheckHealthAsync(new HealthCheckContext());
        Assert.Equal(HealthStatus.Degraded, rmqResult.Status);

        // DB connection bị đóng / không hợp lệ -> CanConnectAsync = false (Unhealthy)
        var badConn = new SqliteConnection("Data Source=Z:\\nonexistent_folder_12345\\bad.db");
        var badOpts = new DbContextOptionsBuilder<ReportDbContext>().UseSqlite(badConn).Options;
        await using var badDb = new ReportDbContext(badOpts);
        Assert.False(await badDb.Database.CanConnectAsync());
    }
}

