using System.ComponentModel.DataAnnotations;
using TransactionService.Models;

namespace TransactionService.DTOs;

public class CreateTransactionRequest
{
    [Required(ErrorMessage = "WalletId không được để trống")]
    public Guid WalletId { get; set; }

    public Guid? CategoryId { get; set; }

    [Range(1, 2, ErrorMessage = "Loại giao dịch người dùng tạo chỉ có thể là 1 (Chi tiêu) hoặc 2 (Thu nhập)")]
    public int Type { get; set; }

    [Range(0.01, 1_000_000_000_000, ErrorMessage = "Số tiền phải lớn hơn 0 và tối đa 1.000 tỷ")]
    public decimal Amount { get; set; }

    public DateTime TransactionDate { get; set; } = DateTime.UtcNow;

    [MaxLength(1000, ErrorMessage = "Ghi chú tối đa 1000 ký tự")]
    public string? Note { get; set; }
}

public class UpdateTransactionRequest
{
    [Required(ErrorMessage = "WalletId không được để trống")]
    public Guid WalletId { get; set; }

    public Guid? CategoryId { get; set; }

    [Range(1, 2, ErrorMessage = "Loại giao dịch người dùng sửa chỉ có thể là 1 (Chi tiêu) hoặc 2 (Thu nhập)")]
    public int Type { get; set; }

    [Range(0.01, 1_000_000_000_000, ErrorMessage = "Số tiền phải lớn hơn 0 và tối đa 1.000 tỷ")]
    public decimal Amount { get; set; }

    public DateTime TransactionDate { get; set; }

    [MaxLength(1000, ErrorMessage = "Ghi chú tối đa 1000 ký tự")]
    public string? Note { get; set; }

    public Guid? RowVersion { get; set; }
}

public class TransactionQueryParameters
{
    private int _page = 1;
    public int Page
    {
        get => _page;
        set => _page = value < 1 ? 1 : value;
    }

    private int _pageSize = 20;
    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = value switch
        {
            < 1 => 1,
            > 100 => 100,
            _ => value
        };
    }

    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public Guid? WalletId { get; set; }
    public Guid? CategoryId { get; set; }
    public int? Type { get; set; }
    public decimal? MinAmount { get; set; }
    public decimal? MaxAmount { get; set; }
    public string? Keyword { get; set; }
    public string? SortBy { get; set; } = "transactionDate";
    public string? SortOrder { get; set; } = "desc";
}

public class TransactionDto
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid WalletId { get; set; }
    public Guid? CategoryId { get; set; }
    public int Type { get; set; }
    public string TypeName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTime TransactionDate { get; set; }
    public string? Note { get; set; }
    public string? ReceiptImageUrl { get; set; }
    public Guid RowVersion { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class PagedResult<T>
{
    public List<T> Items { get; set; } = new();
    public int TotalItems { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)TotalItems / PageSize) : 0;
}

public class ReassignCategoryRequest
{
    [Required]
    public Guid OldCategoryId { get; set; }

    [Required]
    public Guid ReplacementCategoryId { get; set; }
}

