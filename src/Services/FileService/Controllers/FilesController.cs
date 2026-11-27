using System.Security.Cryptography;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FileService.Data;
using FileService.DTOs;
using FileService.Models;

namespace FileService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class FilesController : ControllerBase
{
    private const long MaxFileSizeBytes = 5 * 1024 * 1024; // 5 MB
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp"
    };

    private readonly FileDbContext _context;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<FilesController> _logger;

    public FilesController(FileDbContext context, IWebHostEnvironment environment, ILogger<FilesController> logger)
    {
        _context = context;
        _environment = environment;
        _logger = logger;
    }

    [HttpPost("receipts")]
    [Authorize]
    [RequestSizeLimit(6 * 1024 * 1024)]
    public async Task<IActionResult> UploadReceipt(IFormFile? file)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new { message = "File tải lên không được để trống." });
        }

        if (file.Length > MaxFileSizeBytes)
        {
            return BadRequest(new { message = "Dung lượng file tối đa là 5MB." });
        }

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (string.IsNullOrEmpty(ext) || !AllowedExtensions.Contains(ext))
        {
            return BadRequest(new { message = "Định dạng file không được hỗ trợ. Chỉ chấp nhận ảnh PNG, JPEG, WEBP." });
        }

        // Validate magic bytes
        byte[] headerBytes = new byte[16];
        using (var stream = file.OpenReadStream())
        {
            var bytesRead = await stream.ReadAsync(headerBytes, 0, headerBytes.Length);
            if (bytesRead < 4)
            {
                return BadRequest(new { message = "Nội dung file không hợp lệ." });
            }
        }

        var (isValidType, detectedContentType, normalizedExt) = DetectImageType(headerBytes, ext);
        if (!isValidType)
        {
            return BadRequest(new { message = "File không phải là ảnh hợp lệ hoặc bị đổi phần mở rộng." });
        }

        var userId = GetCurrentUserId();
        var fileId = Guid.NewGuid();

        var uploadsFolder = Path.Combine(_environment.ContentRootPath, "uploads", "receipts");
        Directory.CreateDirectory(uploadsFolder);

        var safeFileName = $"{fileId}{normalizedExt}";
        var storagePath = Path.Combine(uploadsFolder, safeFileName);

        string hashHex;
        using (var sha256 = SHA256.Create())
        using (var outputStream = System.IO.File.Create(storagePath))
        using (var inputStream = file.OpenReadStream())
        {
            await inputStream.CopyToAsync(outputStream);
            outputStream.Position = 0;
            var hashBytes = await sha256.ComputeHashAsync(outputStream);
            hashHex = Convert.ToHexString(hashBytes).ToLowerInvariant();
        }

        var metadata = new FileMetadata
        {
            Id = fileId,
            OwnerUserId = userId,
            Purpose = 1, // Receipt
            OriginalFileName = Path.GetFileName(file.FileName),
            StoragePath = storagePath,
            StorageProvider = "Local",
            ContentType = detectedContentType,
            SizeBytes = file.Length,
            Sha256 = hashHex,
            IsEncrypted = false,
            CreatedAt = DateTime.UtcNow
        };

        await _context.FileMetadatas.AddAsync(metadata);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Đã tải lên hóa đơn {FileId} cho User {UserId}, dung lượng {Size} bytes", fileId, userId, file.Length);

        return StatusCode(201, new UploadReceiptResponse { Id = fileId });
    }

    [HttpGet("{id}")]
    [Authorize]
    public async Task<IActionResult> GetFile([FromRoute] string id)
    {
        if (!Guid.TryParse(id, out var fileId))
        {
            return NotFound(new { message = "File không tồn tại." });
        }

        var userId = GetCurrentUserId();
        var metadata = await _context.FileMetadatas.FirstOrDefaultAsync(f => f.Id == fileId && f.DeletedAt == null);

        // D53 & RC-04: Nếu file không tồn tại hoặc không thuộc quyền sở hữu của user, trả về 404
        if (metadata == null || metadata.OwnerUserId != userId)
        {
            return NotFound(new { message = "File không tồn tại." });
        }

        if (!System.IO.File.Exists(metadata.StoragePath))
        {
            _logger.LogWarning("File vật lý không tìm thấy tại {Path} cho FileMetadata {Id}", metadata.StoragePath, metadata.Id);
            return NotFound(new { message = "File không tồn tại." });
        }

        var fileBytes = await System.IO.File.ReadAllBytesAsync(metadata.StoragePath);
        return File(fileBytes, metadata.ContentType, metadata.OriginalFileName);
    }

    [HttpGet("/internal/files/{id:guid}")]
    [Authorize(Policy = "SystemOnly")]
    public async Task<IActionResult> GetInternalFileMetadata([FromRoute] Guid id)
    {
        var metadata = await _context.FileMetadatas.FirstOrDefaultAsync(f => f.Id == id && f.DeletedAt == null);
        if (metadata == null)
        {
            return NotFound(new { message = "File không tồn tại." });
        }

        return Ok(new
        {
            id = metadata.Id,
            ownerUserId = metadata.OwnerUserId,
            purpose = metadata.Purpose,
            contentType = metadata.ContentType,
            sizeBytes = metadata.SizeBytes,
            relatedEntityType = metadata.RelatedEntityType,
            relatedEntityId = metadata.RelatedEntityId
        });
    }

    private static (bool IsValid, string ContentType, string Extension) DetectImageType(byte[] header, string claimedExt)
    {
        // PNG magic bytes: 89 50 4E 47
        if (header.Length >= 4 && header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47)
        {
            return (claimedExt == ".png", "image/png", ".png");
        }

        // JPEG magic bytes: FF D8 FF
        if (header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
        {
            return (claimedExt == ".jpg" || claimedExt == ".jpeg", "image/jpeg", ".jpg");
        }

        // WEBP: 'RIFF'....'WEBP'
        if (header.Length >= 12 &&
            header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46 &&
            header[8] == 0x57 && header[9] == 0x45 && header[10] == 0x42 && header[11] == 0x50)
        {
            return (claimedExt == ".webp", "image/webp", ".webp");
        }

        return (false, string.Empty, string.Empty);
    }

    private Guid GetCurrentUserId()
    {
        var idStr = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(idStr, out var id) ? id : Guid.Empty;
    }
}
