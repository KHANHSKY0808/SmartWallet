using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Events.EventContracts;
using FileService.Consumers;
using FileService.Controllers;
using FileService.Data;
using FileService.DTOs;
using FileService.Models;
using FileService.Services;

namespace FileService.Tests;

public class MockWebHostEnvironment : IWebHostEnvironment
{
    public string WebRootPath { get; set; } = string.Empty;
    public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } = null!;
    public string EnvironmentName { get; set; } = "Development";
    public string ApplicationName { get; set; } = "FileService";
    public string ContentRootPath { get; set; } = Path.Combine(Path.GetTempPath(), "FileServiceTests_" + Guid.NewGuid().ToString("N"));
    public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
}

public class FilesControllerTests : IDisposable
{
    private static readonly byte[] ValidPngBytes = new byte[]
    {
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41,
        0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
        0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00,
        0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
        0x42, 0x60, 0x82
    };

    private readonly SqliteConnection _connection;
    private readonly FileDbContext _context;
    private readonly MockWebHostEnvironment _env;
    private readonly FilesController _controller;
    private readonly Guid _userA = Guid.NewGuid();
    private readonly Guid _userB = Guid.NewGuid();

    public FilesControllerTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<FileDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new FileDbContext(options);
        _context.Database.EnsureCreated();

        _env = new MockWebHostEnvironment();
        Directory.CreateDirectory(_env.ContentRootPath);

        _controller = new FilesController(_context, _env, NullLogger<FilesController>.Instance);
        SetUser(_userA);
    }

    private void SetUser(Guid userId)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new("sub", userId.ToString())
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
        };
    }

    private static IFormFile CreateFormFile(byte[] content, string fileName, string contentType)
    {
        var stream = new MemoryStream(content);
        return new FormFile(stream, 0, content.Length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };
    }

    [Fact]
    public async Task RC01_UploadPngReal1KB_Returns201_GuidFileName()
    {
        var file = CreateFormFile(ValidPngBytes, "hoadon.png", "image/png");

        var result = await _controller.UploadReceipt(file) as ObjectResult;
        Assert.NotNull(result);
        Assert.Equal(201, result.StatusCode);

        var val = Assert.IsType<UploadReceiptResponse>(result.Value);
        Guid fileId = val.Id;
        Assert.NotEqual(Guid.Empty, fileId);

        var savedMeta = await _context.FileMetadatas.FindAsync(fileId);
        Assert.NotNull(savedMeta);
        Assert.Equal(_userA, savedMeta.OwnerUserId);
        Assert.Equal(1, savedMeta.Purpose);
        Assert.Equal("hoadon.png", savedMeta.OriginalFileName);
        Assert.Equal("image/png", savedMeta.ContentType);
        Assert.Contains(fileId.ToString(), savedMeta.StoragePath);
        Assert.DoesNotContain(_userA.ToString(), savedMeta.StoragePath); // Does NOT contain UserId in storage path
        Assert.True(File.Exists(savedMeta.StoragePath));
    }

    [Fact]
    public async Task RC02_InvalidUploads_Over5MB_Gif_HtmlDisguised_Empty_Returns400()
    {
        // 1. File rỗng
        var emptyFile = CreateFormFile(Array.Empty<byte>(), "empty.png", "image/png");
        var res1 = await _controller.UploadReceipt(emptyFile) as BadRequestObjectResult;
        Assert.NotNull(res1);
        Assert.Equal(400, res1.StatusCode);

        // 2. File > 5MB
        var largeBytes = new byte[5 * 1024 * 1024 + 10];
        Array.Copy(ValidPngBytes, largeBytes, ValidPngBytes.Length);
        var largeFile = CreateFormFile(largeBytes, "large.png", "image/png");
        var res2 = await _controller.UploadReceipt(largeFile) as BadRequestObjectResult;
        Assert.NotNull(res2);
        Assert.Equal(400, res2.StatusCode);

        // 3. GIF
        var gifBytes = Encoding.ASCII.GetBytes("GIF89a1234567890");
        var gifFile = CreateFormFile(gifBytes, "test.gif", "image/gif");
        var res3 = await _controller.UploadReceipt(gifFile) as BadRequestObjectResult;
        Assert.NotNull(res3);
        Assert.Equal(400, res3.StatusCode);

        // 4. HTML disguised as .png
        var htmlBytes = Encoding.UTF8.GetBytes("<html><script>alert(1)</script></html>");
        var htmlFile = CreateFormFile(htmlBytes, "fake.png", "image/png");
        var res4 = await _controller.UploadReceipt(htmlFile) as BadRequestObjectResult;
        Assert.NotNull(res4);
        Assert.Equal(400, res4.StatusCode);
    }

    [Fact]
    public async Task RC03_OwnerDownloadsFile_Returns200WithMatchingBytesAndContentType()
    {
        var file = CreateFormFile(ValidPngBytes, "receipt.png", "image/png");
        var uploadRes = await _controller.UploadReceipt(file) as ObjectResult;
        var val = Assert.IsType<UploadReceiptResponse>(uploadRes!.Value);
        Guid fileId = val.Id;

        var downloadRes = await _controller.GetFile(fileId.ToString()) as FileContentResult;
        Assert.NotNull(downloadRes);
        Assert.Equal("image/png", downloadRes.ContentType);
        Assert.Equal(ValidPngBytes, downloadRes.FileContents);
    }

    [Fact]
    public async Task RC04_ForeignUser_Returns404()
    {
        var file = CreateFormFile(ValidPngBytes, "receipt.png", "image/png");
        var uploadRes = await _controller.UploadReceipt(file) as ObjectResult;
        var val = Assert.IsType<UploadReceiptResponse>(uploadRes!.Value);
        Guid fileId = val.Id;

        // Switch to User B
        SetUser(_userB);

        var getRes = await _controller.GetFile(fileId.ToString()) as NotFoundObjectResult;
        Assert.NotNull(getRes);
        Assert.Equal(404, getRes.StatusCode);
    }

    [Theory]
    [InlineData("..%2F..%2Fetc%2Fpasswd")]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task RC06_PathTraversalOrInvalidGuid_Returns404(string badId)
    {
        var result = await _controller.GetFile(badId) as NotFoundObjectResult;
        Assert.NotNull(result);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task RC07_TransactionDeleted_OrphansFile()
    {
        var fileId = Guid.NewGuid();
        var meta = new FileMetadata
        {
            Id = fileId,
            OwnerUserId = _userA,
            Purpose = 1,
            OriginalFileName = "rc.png",
            StoragePath = "uploads/receipts/rc.png",
            ContentType = "image/png",
            SizeBytes = 100,
            RelatedEntityType = "Transaction",
            RelatedEntityId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow
        };
        await _context.FileMetadatas.AddAsync(meta);
        await _context.SaveChangesAsync();

        var services = new ServiceCollection();
        services.AddSingleton(_context);
        var serviceProvider = services.BuildServiceProvider();

        var config = new ConfigurationBuilder().Build();
        var consumer = new TransactionDeletedConsumer(config, NullLogger<TransactionDeletedConsumer>.Instance, serviceProvider);

        var delEvent = new TransactionDeletedEvent
        {
            TransactionId = Guid.NewGuid(),
            UserId = _userA,
            WalletId = Guid.NewGuid(),
            CategoryId = Guid.NewGuid(),
            Type = 1,
            Amount = 10_000,
            ReceiptFileId = fileId
        };

        await consumer.HandleEventAsync(delEvent);

        var updated = await _context.FileMetadatas.FindAsync(fileId);
        Assert.NotNull(updated);
        Assert.Null(updated.RelatedEntityId);
        Assert.NotNull(updated.ExpiresAt);
        Assert.True(updated.ExpiresAt > DateTime.UtcNow);
    }

    [Fact]
    public async Task OrphanFileCleanup_RemovesExpiredOrphans_RC07()
    {
        var tempFolder = Path.Combine(_env.ContentRootPath, "uploads", "receipts");
        Directory.CreateDirectory(tempFolder);
        var testFilePath = Path.Combine(tempFolder, "orphan-test.png");
        await System.IO.File.WriteAllTextAsync(testFilePath, "test dummy content");

        var fileId = Guid.NewGuid();
        var expiredOrphan = new FileMetadata
        {
            Id = fileId,
            OwnerUserId = _userA,
            Purpose = 1,
            OriginalFileName = "orphan-test.png",
            StoragePath = testFilePath,
            ContentType = "image/png",
            SizeBytes = 100,
            RelatedEntityType = null,
            RelatedEntityId = null,
            CreatedAt = DateTime.UtcNow.AddDays(-10),
            ExpiresAt = DateTime.UtcNow.AddDays(-1) // expired
        };

        await _context.FileMetadatas.AddAsync(expiredOrphan);
        await _context.SaveChangesAsync();

        Assert.True(System.IO.File.Exists(testFilePath));

        var services = new ServiceCollection();
        services.AddSingleton(_context);
        var serviceProvider = services.BuildServiceProvider();

        var cleanupService = new OrphanFileCleanupService(serviceProvider, NullLogger<OrphanFileCleanupService>.Instance);
        await cleanupService.CleanupOrphanFilesAsync();

        // Verify physical file was deleted
        Assert.False(System.IO.File.Exists(testFilePath));

        // Verify DB record was removed
        var recordInDb = await _context.FileMetadatas.FindAsync(fileId);
        Assert.Null(recordInDb);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
        if (Directory.Exists(_env.ContentRootPath))
        {
            try { Directory.Delete(_env.ContentRootPath, true); } catch { }
        }
    }
}
