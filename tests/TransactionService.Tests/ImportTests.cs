using System.Diagnostics;
using System.Security.Claims;
using System.Text;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Events;
using SharedKernel.Messaging;
using TransactionService.Controllers;
using TransactionService.Data;
using TransactionService.DTOs;
using TransactionService.Models;
using TransactionService.Services;
using Xunit;
using Xunit.Abstractions;

namespace TransactionService.Tests;

public class ImportTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly SqliteConnection _connection;
    private readonly string _tempDir;
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _walletId = Guid.NewGuid();
    private readonly Guid _expCatId = Guid.NewGuid();
    private readonly Guid _incCatId = Guid.NewGuid();

    public ImportTests(ITestOutputHelper output)
    {
        _output = output;
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();
        _tempDir = Path.Combine(Path.GetTempPath(), $"qlct-imp-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        using var db = CreateDbContext();
        db.Database.EnsureCreated();
    }

    private TransactionDbContext CreateDbContext()
    {
        var opts = new DbContextOptionsBuilder<TransactionDbContext>().UseSqlite(_connection).Options;
        return new TransactionDbContext(opts);
    }

    public void Dispose()
    {
        _connection.Dispose();
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void Test_IM02_IM03_IM06_IM10_ImportParser_CsvXlsxValidationAndSanitization()
    {
        // IM-02: BOM + ';' + Vietnamese headers + spaced thousands
        var csvContent = "\uFEFFNgày;Số tiền;Loại;Danh mục;Ví;Ghi chú\r\n15/11/2026;1.500.000;Chi;Ăn uống;Ví chính;\"có ; trong ghi chú\"\r\n";
        var parsedCsv = ImportParser.ParseFile(Encoding.UTF8.GetBytes(csvContent), ".csv");
        Assert.Single(parsedCsv);
        Assert.Equal("15/11/2026", parsedCsv[0].DateRaw);
        Assert.Equal("1.500.000", parsedCsv[0].AmountRaw);
        Assert.Equal("có ; trong ghi chú", parsedCsv[0].NoteRaw);

        // IM-03: Excel .xlsx
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Sheet1");
        ws.Cell(1, 1).Value = "Ngay"; ws.Cell(1, 2).Value = "SoTien"; ws.Cell(1, 3).Value = "Loai";
        ws.Cell(1, 4).Value = "DanhMuc"; ws.Cell(1, 5).Value = "Vi"; ws.Cell(1, 6).Value = "GhiChu";
        ws.Cell(2, 1).Value = "2026-11-10"; ws.Cell(2, 2).Value = 200000; ws.Cell(2, 3).Value = "Chi";
        ws.Cell(2, 4).Value = "Ăn uống"; ws.Cell(2, 5).Value = "Ví chính"; ws.Cell(2, 6).Value = "xlsx row";
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        var parsedXlsx = ImportParser.ParseFile(ms.ToArray(), ".xlsx");
        Assert.Single(parsedXlsx);
        Assert.Equal("200000", parsedXlsx[0].AmountRaw);

        // IM-06: CSV injection neutralization
        Assert.Equal("'=HYPERLINK(\"http://evil\",\"x\")", ImportParser.SanitizeCsvCell("=HYPERLINK(\"http://evil\",\"x\")"));
        Assert.Equal("'+SUM(A1)", ImportParser.SanitizeCsvCell("+SUM(A1)"));
        Assert.Equal("'-1+1", ImportParser.SanitizeCsvCell("-1+1"));
        Assert.Equal("'@cmd", ImportParser.SanitizeCsvCell("@cmd"));

        // IM-10: Invalid UTF-8 bytes & missing columns & > 10,000 rows
        var invalidUtf8 = new byte[] { 0x4E, 0x67, 0x61, 0x79, 0xFF, 0xFE };
        Assert.Throws<ImportValidationException>(() => ImportParser.ParseFile(invalidUtf8, ".csv"));
        // Windows-1258 decomposed Vietnamese bytes ("Ngay,SoTien\r\n01/11/2026,Ăn pho\r\n" -> 0xC3 'Ă' invalid in UTF-8)
        var cp1258Bytes = new byte[] { 0x4E, 0x67, 0x61, 0x79, 0x2C, 0x53, 0x6F, 0x54, 0x69, 0x65, 0x6E, 0x0D, 0x0A, 0x30, 0x31, 0x2F, 0x31, 0x31, 0x2F, 0x32, 0x30, 0x32, 0x36, 0x2C, 0xC3, 0x6E, 0x20, 0x70, 0x68, 0x6F, 0x0D, 0x0A };
        Assert.Throws<ImportValidationException>(() => ImportParser.ParseFile(cp1258Bytes, ".csv"));
        Assert.Throws<ImportValidationException>(() => ImportParser.ParseFile(Encoding.UTF8.GetBytes("Ngay,SoTien\r\n01/11/2026,1000\r\n"), ".csv"));

        var tooManyRows = new StringBuilder("Ngay,SoTien,Loai,DanhMuc,Vi\r\n");
        for (int i = 0; i < 10_001; i++) tooManyRows.AppendLine("01/11/2026,1000,Chi,Ăn uống,Ví chính");
        Assert.Throws<ImportValidationException>(() => ImportParser.ParseFile(Encoding.UTF8.GetBytes(tooManyRows.ToString()), ".csv"));
    }

    [Fact]
    public async Task Test_IM08_IM11_Batch10000Rows_Under5Minutes_AndResumeWithoutDuplicates()
    {
        var publisher = new FakeEventPublisher();
        var services = new ServiceCollection();
        services.AddScoped(_ => CreateDbContext());
        services.AddSingleton<IWalletClient>(new FakeImportWalletClient(_walletId));
        services.AddSingleton<ICategoryClient>(new FakeImportCategoryClient(_expCatId, _incCatId));
        services.AddSingleton<IEventPublisher>(publisher);
        using var sp = services.BuildServiceProvider();

        // IM-11: Build 10,000 rows CSV and run through ImportBackgroundService
        var sb = new StringBuilder("Ngay,SoTien,Loai,DanhMuc,Vi,GhiChu\r\n");
        for (int i = 0; i < 10_000; i++)
        {
            sb.AppendLine($"15/11/2026,1000,Chi,Ăn uống,Ví chính,row {i}");
        }
        var tempFile = Path.Combine(_tempDir, "10k.csv");
        await File.WriteAllBytesAsync(tempFile, Encoding.UTF8.GetBytes(sb.ToString()));

        var jobId = Guid.NewGuid();
        await using (var db = CreateDbContext())
        {
            db.ImportJobs.Add(new ImportJob
            {
                Id = jobId,
                UserId = _userId,
                FileName = "10k.csv",
                TempFilePath = tempFile,
                Extension = ".csv",
                TotalRows = 10_000,
                Status = ImportJobStatus.Queued
            });
            await db.SaveChangesAsync();
        }

        var worker = new ImportBackgroundService(sp, new ImportJobQueue(), NullLogger<ImportBackgroundService>.Instance);
        var sw = Stopwatch.StartNew();
        await worker.ProcessJobAsync(jobId);
        sw.Stop();
        _output.WriteLine($"IM-11 benchmark: 10,000 rows imported in {sw.Elapsed.TotalMilliseconds:F1} ms");
        Assert.True(sw.Elapsed < TimeSpan.FromMinutes(5), $"IM-11 took {sw.Elapsed}");

        await using (var db = CreateDbContext())
        {
            var job = await db.ImportJobs.FindAsync(jobId);
            Assert.NotNull(job);
            Assert.Equal(ImportJobStatus.Completed, job!.Status);
            Assert.Equal(10_000, job.Succeeded);
            Assert.Equal(0, job.Failed);
            Assert.Equal(10_000, await db.Transactions.CountAsync(t => t.SourceType == SourceType.Import && t.SourceId == jobId));
            Assert.False(File.Exists(tempFile)); // Temp file deleted after completion
        }

        // IM-08 resume check: simulate partially completed job restarted with existing 50 rows already committed
        var resumeFile = Path.Combine(_tempDir, "resume.csv");
        var rsb = new StringBuilder("Ngay,SoTien,Loai,DanhMuc,Vi,GhiChu\r\n");
        for (int i = 1; i <= 150; i++) rsb.AppendLine($"16/11/2026,2000,Chi,Ăn uống,Ví chính,resume {i}");
        await File.WriteAllBytesAsync(resumeFile, Encoding.UTF8.GetBytes(rsb.ToString()));

        var resumeJobId = Guid.NewGuid();
        publisher.Events.Clear();
        await using (var db = CreateDbContext())
        {
            db.ImportJobs.Add(new ImportJob
            {
                Id = resumeJobId,
                UserId = _userId,
                FileName = "resume.csv",
                TempFilePath = resumeFile,
                Extension = ".csv",
                TotalRows = 150,
                Succeeded = 50,
                Status = ImportJobStatus.Processing
            });
            for (int i = 1; i <= 50; i++)
            {
                var subId = ImportParser.DeterministicRowGuid(resumeJobId, i);
                db.Transactions.Add(new Transaction
                {
                    Id = subId,
                    UserId = _userId,
                    WalletId = _walletId,
                    CategoryId = _expCatId,
                    Type = TransactionType.Expense,
                    Amount = 2000,
                    TransactionDate = DateTime.UtcNow,
                    SourceType = SourceType.Import,
                    SourceId = resumeJobId,
                    SourceSubId = subId
                });
            }
            await db.SaveChangesAsync();
        }

        await worker.ProcessJobAsync(resumeJobId);
        await using (var db = CreateDbContext())
        {
            var job = await db.ImportJobs.FindAsync(resumeJobId);
            Assert.Equal(ImportJobStatus.Completed, job!.Status);
            Assert.Equal(150, job.Succeeded);
            Assert.Equal(150, await db.Transactions.CountAsync(t => t.SourceType == SourceType.Import && t.SourceId == resumeJobId));
            Assert.Equal(100, publisher.Events.Count); // Published ONLY the missing 100 rows
        }
    }

    [Fact]
    public async Task Test_IM04_IM05_IM09_ControllerEndpointsAndSourceTypeFilter()
    {
        await using var db = CreateDbContext();
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Import:TempDirectory"] = _tempDir })
            .Build();
        var queue = new ImportJobQueue();
        var ctrl = new ImportsController(db, queue, cfg, NullLogger<ImportsController>.Instance);

        var otherUserId = Guid.NewGuid();
        db.ImportJobs.Add(new ImportJob
        {
            Id = Guid.NewGuid(),
            UserId = _userId,
            FileName = "active.csv",
            Status = ImportJobStatus.Processing
        });
        var otherJobId = Guid.NewGuid();
        db.ImportJobs.Add(new ImportJob
        {
            Id = otherJobId,
            UserId = otherUserId,
            FileName = "other.csv",
            Status = ImportJobStatus.Completed
        });
        await db.SaveChangesAsync();

        SetUser(ctrl, _userId);
        ctrl.ControllerContext.HttpContext.Request.ContentType = "multipart/form-data; boundary=x";
        ctrl.ControllerContext.HttpContext.Request.Form = new FormCollection(new(), new FormFileCollection());

        // IM-04: Active job -> 409 IMPORT_IN_PROGRESS
        var conflict = Assert.IsType<ConflictObjectResult>(await ctrl.UploadImport(CancellationToken.None));
        Assert.Contains("IMPORT_IN_PROGRESS", System.Text.Json.JsonSerializer.Serialize(conflict.Value));

        // IM-05: Reading other user's job -> 404
        Assert.IsType<NotFoundObjectResult>(await ctrl.GetImportStatus(otherJobId, CancellationToken.None));
        Assert.IsType<NotFoundObjectResult>(await ctrl.DownloadErrorsCsv(otherJobId, CancellationToken.None));

        // IM-09: TransactionsController filters by sourceType=Import
        db.Transactions.Add(new Transaction
        {
            Id = Guid.NewGuid(),
            UserId = _userId,
            WalletId = _walletId,
            CategoryId = _expCatId,
            Type = TransactionType.Expense,
            Amount = 5000,
            SourceType = SourceType.Import,
            SourceId = Guid.NewGuid()
        });
        await db.SaveChangesAsync();

        var txCtrl = new TransactionsController(db, new FakeImportWalletClient(_walletId), new FakeImportCategoryClient(_expCatId, _incCatId), NullLogger<TransactionsController>.Instance, null);
        SetUser(txCtrl, _userId);
        var okRes = Assert.IsType<OkObjectResult>(await txCtrl.GetTransactions(new TransactionQueryParameters { SourceType = "Import" }));
        var paged = Assert.IsType<PagedResult<TransactionDto>>(okRes.Value);
        Assert.Single(paged.Items);
        Assert.Equal("Import", paged.Items[0].SourceType);
    }

    private static void SetUser(ControllerBase controller, Guid uid)
    {
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, uid.ToString()),
                    new Claim("sub", uid.ToString())
                }, "test"))
            }
        };
    }

    private sealed class FakeImportWalletClient : IWalletClient
    {
        private readonly Guid _wid;
        public FakeImportWalletClient(Guid wid) => _wid = wid;
        public Task<WalletValidationResult> ValidateWalletAsync(Guid walletId, string? bearerToken = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new WalletValidationResult(walletId == _wid, false, "VND"));
        public Task<List<ImportWalletInfo>> GetUserWalletsAsync(Guid userId, CancellationToken cancellationToken = default)
            => Task.FromResult(new List<ImportWalletInfo> { new(_wid, "Ví chính", "VND", false) });
    }

    private sealed class FakeImportCategoryClient : ICategoryClient
    {
        private readonly Guid _expId;
        private readonly Guid _incId;
        public FakeImportCategoryClient(Guid expId, Guid incId) { _expId = expId; _incId = incId; }
        public Task<CategoryValidationResult> ValidateCategoryAsync(Guid categoryId, string? bearerToken = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new CategoryValidationResult(true, categoryId == _incId ? 2 : 1, null, "Ăn uống"));
        public Task<List<ImportCategoryInfo>> GetUserCategoriesAsync(Guid userId, CancellationToken cancellationToken = default)
            => Task.FromResult(new List<ImportCategoryInfo>
            {
                new(_expId, null, "Ăn uống", 1, null),
                new(_incId, null, "Lương", 2, null)
            });
    }

    private sealed class FakeEventPublisher : IEventPublisher
    {
        public List<IntegrationEvent> Events { get; } = new();
        public Task PublishAsync<TEvent>(TEvent @event, string routingKey, CancellationToken cancellationToken = default) where TEvent : IntegrationEvent
        {
            Events.Add(@event);
            return Task.CompletedTask;
        }
    }
}

