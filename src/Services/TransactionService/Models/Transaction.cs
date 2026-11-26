using System.ComponentModel.DataAnnotations;

namespace TransactionService.Models;

public enum TransactionType
{
    Expense = 1,
    Income = 2,
    Transfer = 3
}

public enum SourceType
{
    Manual = 1,
    Recurring = 2,
    Debt = 3,
    Goal = 4,
    Import = 5
}

public class Transaction
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    public Guid WalletId { get; set; }

    /// <summary>
    /// CategoryId nullable vì giao dịch chuyển tiền (Type = 3) hoặc giao dịch không phân loại không bắt buộc category.
    /// </summary>
    public Guid? CategoryId { get; set; }

    public TransactionType Type { get; set; }

    public decimal Amount { get; set; }

    public DateTime TransactionDate { get; set; } = DateTime.UtcNow;

    [MaxLength(1000)]
    public string? Note { get; set; }

    [MaxLength(500)]
    public string? ReceiptImageUrl { get; set; }

    public Guid? ReceiptFileId { get; set; }

    public SourceType SourceType { get; set; } = SourceType.Manual;

    public Guid? SourceId { get; set; }

    public Guid? SourceSubId { get; set; }

    public DateOnly? OccurrenceDate { get; set; }

    /// <summary>
    /// Lưu TransferId khi giao dịch được tạo tự động từ sự kiện MoneyTransferred (Type = 3)
    /// Dùng để đảm bảo tính Idempotent (D2, FIN-34b)
    /// </summary>
    public Guid? TransferId { get; set; }

    /// <summary>
    /// Optimistic Concurrency Token: Ứng dụng tự sinh Guid mới mỗi lần cập nhật
    /// </summary>
    [ConcurrencyCheck]
    public Guid RowVersion { get; set; } = Guid.NewGuid();

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

