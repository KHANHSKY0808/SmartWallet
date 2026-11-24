using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TransactionService.Controllers;
using TransactionService.Data;
using TransactionService.DTOs;
using TransactionService.Models;
using TransactionService.Services;

namespace TransactionService.Tests;

public class FakeFileServiceClient : IFileServiceClient
{
    public Func<Guid, FileValidationResult> Validator { get; set; } = id => new FileValidationResult(true, null, 1);

    public Task<FileValidationResult> ValidateFileAsync(Guid fileId, string? bearerToken = null, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Validator(fileId));
    }
}

public class TagsAndReceiptTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly TransactionDbContext _context;
    private readonly FakeWalletClient _walletClient;
    private readonly FakeCategoryClient _categoryClient;
    private readonly FakeFileServiceClient _fileServiceClient;
    private readonly FakeEventPublisher _eventPublisher;
    private readonly TransactionsController _txController;
    private readonly TagsController _tagsController;

    private readonly Guid _userA = Guid.NewGuid();
    private readonly Guid _userB = Guid.NewGuid();
    private readonly Guid _walletA = Guid.NewGuid();
    private readonly Guid _catA = Guid.NewGuid();

    public TagsAndReceiptTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<TransactionDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new TransactionDbContext(options);
        _context.Database.EnsureCreated();

        _walletClient = new FakeWalletClient();
        _categoryClient = new FakeCategoryClient();
        _fileServiceClient = new FakeFileServiceClient();
        _eventPublisher = new FakeEventPublisher();

        _txController = new TransactionsController(
            _context,
            _walletClient,
            _categoryClient,
            NullLogger<TransactionsController>.Instance,
            _fileServiceClient,
            _eventPublisher);

        _tagsController = new TagsController(_context, NullLogger<TagsController>.Instance);

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
        var httpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
        _txController.ControllerContext = new ControllerContext { HttpContext = httpContext };
        _tagsController.ControllerContext = new ControllerContext { HttpContext = httpContext };
    }

    [Fact]
    public async Task TG01_CreateTag_TrimAndUniqueCaseInsensitive_Returns201And409()
    {
        var res1 = await _tagsController.CreateTag(new CreateTagRequest { Name = "Công tác" }) as ObjectResult;
        Assert.NotNull(res1);
        Assert.Equal(201, res1.StatusCode);

        var res2 = await _tagsController.CreateTag(new CreateTagRequest { Name = "  công TÁC " }) as ConflictObjectResult;
        Assert.NotNull(res2);
        Assert.Equal(409, res2.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("123456789012345678901234567890123456789012345678901")] // 51 chars
    public async Task TG02_CreateTag_Validation_EmptyOrOver50_Returns400(string name)
    {
        var res = await _tagsController.CreateTag(new CreateTagRequest { Name = name }) as BadRequestObjectResult;
        Assert.NotNull(res);
        Assert.Equal(400, res.StatusCode);
    }

    [Fact]
    public async Task TG03_Attach11Tags_Returns400()
    {
        var tagIds = new List<Guid>();
        for (int i = 0; i < 11; i++)
        {
            var tag = new Tag { Id = Guid.NewGuid(), UserId = _userA, Name = $"Tag_{i}" };
            await _context.Tags.AddAsync(tag);
            tagIds.Add(tag.Id);
        }
        await _context.SaveChangesAsync();

        var res = await _txController.CreateTransaction(new CreateTransactionRequest
        {
            WalletId = _walletA,
            CategoryId = _catA,
            Type = 1,
            Amount = 1000,
            TagIds = tagIds
        }) as BadRequestObjectResult;

        Assert.NotNull(res);
        Assert.Equal(400, res.StatusCode);
    }

    [Fact]
    public async Task TG04_TG06_TagsArePrivate_ForeignTagRejected400_NotVisibleInGet()
    {
        // Tag of User B
        var tagB = new Tag { Id = Guid.NewGuid(), UserId = _userB, Name = "Tag của B" };
        await _context.Tags.AddAsync(tagB);
        await _context.SaveChangesAsync();

        // User A tries to use Tag of B
        var res = await _txController.CreateTransaction(new CreateTransactionRequest
        {
            WalletId = _walletA,
            CategoryId = _catA,
            Type = 1,
            Amount = 1000,
            TagIds = new List<Guid> { tagB.Id }
        }) as BadRequestObjectResult;

        Assert.NotNull(res);
        Assert.Equal(400, res.StatusCode);

        // User A calls GET tags -> B's tag not visible
        var listRes = await _tagsController.GetTags() as OkObjectResult;
        Assert.NotNull(listRes);
        var tags = Assert.IsAssignableFrom<IEnumerable<TagDto>>(listRes.Value);
        Assert.DoesNotContain(tags, t => t.Id == tagB.Id);
    }

    [Fact]
    public async Task TG05_FilterByTag_ReturnsMatchingTransactions()
    {
        var tag = new Tag { Id = Guid.NewGuid(), UserId = _userA, Name = "Công tác" };
        await _context.Tags.AddAsync(tag);
        await _context.SaveChangesAsync();

        // Create 3 transactions with tag
        for (int i = 0; i < 3; i++)
        {
            await _txController.CreateTransaction(new CreateTransactionRequest
            {
                WalletId = _walletA,
                CategoryId = _catA,
                Type = 1,
                Amount = 1000 * (i + 1),
                TagIds = new List<Guid> { tag.Id }
            });
        }

        // Create 2 transactions without tag
        for (int i = 0; i < 2; i++)
        {
            await _txController.CreateTransaction(new CreateTransactionRequest
            {
                WalletId = _walletA,
                CategoryId = _catA,
                Type = 1,
                Amount = 5000 * (i + 1)
            });
        }

        var filterRes = await _txController.GetTransactions(new TransactionQueryParameters
        {
            TagIds = tag.Id.ToString()
        }) as OkObjectResult;

        Assert.NotNull(filterRes);
        var paged = Assert.IsType<PagedResult<TransactionDto>>(filterRes.Value);
        Assert.Equal(3, paged.TotalItems);
    }

    [Fact]
    public async Task TG07_DeleteTag_RemovesTagAndUnlinksFromTransactions()
    {
        var tag = new Tag { Id = Guid.NewGuid(), UserId = _userA, Name = "Xóa thẻ" };
        await _context.Tags.AddAsync(tag);
        await _context.SaveChangesAsync();

        var createRes = await _txController.CreateTransaction(new CreateTransactionRequest
        {
            WalletId = _walletA,
            CategoryId = _catA,
            Type = 1,
            Amount = 2000,
            TagIds = new List<Guid> { tag.Id }
        }) as ObjectResult;
        var txDto = Assert.IsType<TransactionDto>(createRes!.Value);

        // Delete tag
        var delRes = await _tagsController.DeleteTag(tag.Id) as OkObjectResult;
        Assert.NotNull(delRes);
        Assert.Equal(200, delRes.StatusCode);

        // Transaction still exists, tag link removed
        var txAfter = await _context.Transactions.FindAsync(txDto.Id);
        Assert.NotNull(txAfter);
        var link = await _context.TransactionTags.FirstOrDefaultAsync(tt => tt.TransactionId == txDto.Id && tt.TagId == tag.Id);
        Assert.Null(link);
    }

    [Fact]
    public async Task RC05_AttachReceiptRules_SameUser_UniqueAcrossTransactions()
    {
        var fid1 = Guid.NewGuid();
        var fidForeign = Guid.NewGuid();

        _fileServiceClient.Validator = id =>
        {
            if (id == fid1) return new FileValidationResult(true, _userA, 1);
            if (id == fidForeign) return new FileValidationResult(true, _userB, 1); // Belongs to user B
            return new FileValidationResult(false, null, null);
        };

        // Create 2 transactions
        var t1Res = await _txController.CreateTransaction(new CreateTransactionRequest
        {
            WalletId = _walletA,
            CategoryId = _catA,
            Type = 1,
            Amount = 1000
        }) as ObjectResult;
        var t1 = Assert.IsType<TransactionDto>(t1Res!.Value);

        var t2Res = await _txController.CreateTransaction(new CreateTransactionRequest
        {
            WalletId = _walletA,
            CategoryId = _catA,
            Type = 1,
            Amount = 2000
        }) as ObjectResult;
        var t2 = Assert.IsType<TransactionDto>(t2Res!.Value);

        // 1. Attach fid1 to t1 -> 200
        var put1Res = await _txController.UpdateTransaction(t1.Id, new UpdateTransactionRequest
        {
            WalletId = _walletA,
            CategoryId = _catA,
            Type = 1,
            Amount = 1000,
            RowVersion = t1.RowVersion,
            ReceiptFileId = fid1
        }) as OkObjectResult;
        Assert.NotNull(put1Res);
        Assert.Equal(200, put1Res.StatusCode);

        // 2. Attach fid1 to t2 -> 409 (already attached)
        var put2Res = await _txController.UpdateTransaction(t2.Id, new UpdateTransactionRequest
        {
            WalletId = _walletA,
            CategoryId = _catA,
            Type = 1,
            Amount = 2000,
            RowVersion = t2.RowVersion,
            ReceiptFileId = fid1
        }) as ObjectResult;
        Assert.NotNull(put2Res);
        Assert.Equal(409, put2Res.StatusCode);

        // 3. Attach foreign file -> 400
        var putForeignRes = await _txController.UpdateTransaction(t2.Id, new UpdateTransactionRequest
        {
            WalletId = _walletA,
            CategoryId = _catA,
            Type = 1,
            Amount = 2000,
            RowVersion = t2.RowVersion,
            ReceiptFileId = fidForeign
        }) as BadRequestObjectResult;
        Assert.NotNull(putForeignRes);
        Assert.Equal(400, putForeignRes.StatusCode);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}
