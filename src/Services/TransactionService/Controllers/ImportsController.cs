using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TransactionService.Data;
using TransactionService.Models;
using TransactionService.Services;

namespace TransactionService.Controllers;

[ApiController]
[Route("api/transactions/imports")]
[Authorize]
public class ImportsController : ControllerBase
{
    private readonly TransactionDbContext _context;
    private readonly IImportJobQueue _queue;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ImportsController> _logger;

    public ImportsController(
        TransactionDbContext context,
        IImportJobQueue queue,
        IConfiguration configuration,
        ILogger<ImportsController> logger)
    {
        _context = context;
        _queue = queue;
        _configuration = configuration;
        _logger = logger;
    }

    [HttpPost]
    [RequestSizeLimit(20 * 1024 * 1024)]
    public async Task<IActionResult> UploadImport(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty)
        {
            return Unauthorized();
        }

        if (!Request.HasFormContentType)
        {
            return BadRequest(new { message = "Yêu cầu multipart/form-data kèm tệp 'file'." });
        }

        // D68 / IM-04: Mỗi user chỉ có 1 job Queued/Processing
        var hasActive = await _context.ImportJobs.AnyAsync(
            j => j.UserId == userId && (j.Status == ImportJobStatus.Queued || j.Status == ImportJobStatus.Processing),
            cancellationToken);
        if (hasActive)
        {
            return Conflict(new
            {
                code = "IMPORT_IN_PROGRESS",
                message = "Bạn đang có một tiến trình nhập giao dịch đang chạy. Vui lòng đợi hoàn tất."
            });
        }

        var form = await Request.ReadFormAsync(cancellationToken);
        var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
        if (file is null || file.Length == 0)
        {
            return BadRequest(new { message = "Tệp không được để trống." });
        }

        if (file.Length > ImportParser.MaxFileSizeBytes)
        {
            return BadRequest(new { message = "Tệp vượt quá kích thước tối đa 5 MB." });
        }

        var ext = Path.GetExtension(file.FileName)?.ToLowerInvariant();
        if (ext is not (".csv" or ".xlsx"))
        {
            return BadRequest(new { message = "Chỉ hỗ trợ định dạng .csv hoặc .xlsx." });
        }

        var mappingJson = form.TryGetValue("mapping", out var mappingVal) ? mappingVal.ToString() : null;

        byte[] bytes;
        using (var ms = new MemoryStream())
        {
            await file.CopyToAsync(ms, cancellationToken);
            bytes = ms.ToArray();
        }

        List<ImportRawRow> parsedRows;
        try
        {
            parsedRows = ImportParser.ParseFile(bytes, ext, mappingJson);
        }
        catch (ImportValidationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }

        var jobId = Guid.NewGuid();
        var tempRoot = _configuration["Import:TempDirectory"];
        if (string.IsNullOrWhiteSpace(tempRoot))
        {
            tempRoot = Path.Combine(Path.GetTempPath(), "smartwallet-imports");
        }
        Directory.CreateDirectory(tempRoot);
        var tempFilePath = Path.Combine(tempRoot, $"{jobId:N}{ext}");
        await System.IO.File.WriteAllBytesAsync(tempFilePath, bytes, cancellationToken);

        var job = new ImportJob
        {
            Id = jobId,
            UserId = userId,
            FileName = Path.GetFileName(file.FileName),
            TempFilePath = tempFilePath,
            Extension = ext,
            ColumnMappingJson = string.IsNullOrWhiteSpace(mappingJson) ? null : mappingJson,
            TotalRows = parsedRows.Count,
            Status = ImportJobStatus.Queued,
            CreatedAt = DateTime.UtcNow
        };

        try
        {
            _context.ImportJobs.Add(job);
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            TryDeleteFile(tempFilePath);
            return Conflict(new
            {
                code = "IMPORT_IN_PROGRESS",
                message = "Bạn đang có một tiến trình nhập giao dịch đang chạy. Vui lòng đợi hoàn tất."
            });
        }

        await _queue.EnqueueAsync(job.Id, cancellationToken);
        _logger.LogInformation("Đã tiếp nhận ImportJob {JobId} ({TotalRows} dòng) cho user {UserId}", job.Id, job.TotalRows, userId);

        return Accepted(new
        {
            jobId = job.Id,
            status = job.Status.ToString()
        });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetImportStatus(Guid id, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var job = await _context.ImportJobs.AsNoTracking()
            .FirstOrDefaultAsync(j => j.Id == id && j.UserId == userId, cancellationToken);

        if (job is null)
        {
            return NotFound(new { message = "Không tìm thấy tiến trình nhập dữ liệu." });
        }

        var errors = await _context.ImportJobErrors.AsNoTracking()
            .Where(e => e.ImportJobId == job.Id)
            .OrderBy(e => e.RowNumber)
            .Take(100)
            .Select(e => new { row = e.RowNumber, reason = e.Reason })
            .ToListAsync(cancellationToken);

        return Ok(new
        {
            id = job.Id,
            jobId = job.Id,
            fileName = job.FileName,
            status = job.Status.ToString(),
            totalRows = job.TotalRows,
            succeeded = job.Succeeded,
            failed = job.Failed,
            errorMessage = job.ErrorMessage,
            errors,
            createdAt = job.CreatedAt,
            completedAt = job.CompletedAt
        });
    }

    [HttpGet]
    public async Task<IActionResult> GetRecentImports(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var jobs = await _context.ImportJobs.AsNoTracking()
            .Where(j => j.UserId == userId)
            .OrderByDescending(j => j.CreatedAt)
            .Take(20)
            .Select(j => new
            {
                id = j.Id,
                jobId = j.Id,
                fileName = j.FileName,
                status = j.Status.ToString(),
                totalRows = j.TotalRows,
                succeeded = j.Succeeded,
                failed = j.Failed,
                createdAt = j.CreatedAt,
                completedAt = j.CompletedAt
            })
            .ToListAsync(cancellationToken);

        return Ok(jobs);
    }

    [HttpGet("{id:guid}/errors.csv")]
    public async Task<IActionResult> DownloadErrorsCsv(Guid id, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var job = await _context.ImportJobs.AsNoTracking()
            .FirstOrDefaultAsync(j => j.Id == id && j.UserId == userId, cancellationToken);

        if (job is null)
        {
            return NotFound(new { message = "Không tìm thấy tiến trình nhập dữ liệu." });
        }

        var errors = await _context.ImportJobErrors.AsNoTracking()
            .Where(e => e.ImportJobId == job.Id)
            .OrderBy(e => e.RowNumber)
            .ToListAsync(cancellationToken);

        var sb = new StringBuilder();
        sb.AppendLine("Ngay,SoTien,Loai,DanhMuc,Vi,GhiChu,Dong,LyDo");
        foreach (var e in errors)
        {
            sb.Append(ImportParser.EscapeCsvCell(e.DateRaw)).Append(',')
              .Append(ImportParser.EscapeCsvCell(e.AmountRaw)).Append(',')
              .Append(ImportParser.EscapeCsvCell(e.TypeRaw)).Append(',')
              .Append(ImportParser.EscapeCsvCell(e.CategoryRaw)).Append(',')
              .Append(ImportParser.EscapeCsvCell(e.WalletRaw)).Append(',')
              .Append(ImportParser.EscapeCsvCell(e.NoteRaw)).Append(',')
              .Append(e.RowNumber).Append(',')
              .AppendLine(ImportParser.EscapeCsvCell(e.Reason));
        }

        var bom = Encoding.UTF8.GetPreamble();
        var body = Encoding.UTF8.GetBytes(sb.ToString());
        var result = new byte[bom.Length + body.Length];
        Buffer.BlockCopy(bom, 0, result, 0, bom.Length);
        Buffer.BlockCopy(body, 0, result, bom.Length, body.Length);

        return File(result, "text/csv; charset=utf-8", $"import-errors-{job.Id:N}.csv");
    }

    private Guid GetCurrentUserId()
    {
        var idStr = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(idStr, out var id) ? id : Guid.Empty;
    }

    private static void TryDeleteFile(string path)
    {
        try { if (System.IO.File.Exists(path)) System.IO.File.Delete(path); } catch { }
    }
}

