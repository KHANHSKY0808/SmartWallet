using System.ComponentModel.DataAnnotations;
using WalletService.Models;

namespace WalletService.DTOs;

public class CreateWalletRequest
{
    [Required(ErrorMessage = "Tên ví không được để trống")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "Tên ví tối đa 100 ký tự")]
    public string Name { get; set; } = string.Empty;

    [Range(1, 4, ErrorMessage = "Loại ví không hợp lệ (1=Tiền mặt, 2=Ngân hàng, 3=Ví điện tử, 4=Thẻ tín dụng)")]
    public int Type { get; set; }

    [Range(0, double.MaxValue, ErrorMessage = "Số dư ban đầu không được âm")]
    [System.Text.Json.Serialization.JsonPropertyName("openingBalance")]
    public decimal InitialBalance { get; set; }

    [StringLength(10, ErrorMessage = "Mã tiền tệ tối đa 10 ký tự")]
    public string? Currency { get; set; }
}

public class UpdateWalletRequest
{
    [Required(ErrorMessage = "Tên ví không được để trống")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "Tên ví tối đa 100 ký tự")]
    public string Name { get; set; } = string.Empty;

    [StringLength(10, ErrorMessage = "Mã tiền tệ tối đa 10 ký tự")]
    public string? Currency { get; set; }

    // Trường Balance nếu client gửi lên sẽ bị bỏ qua theo D1
    public decimal? Balance { get; set; }
}

public class WalletDto
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public WalletType Type { get; set; }
    public decimal Balance { get; set; }
    public decimal InitialBalance { get; set; }
    public string Currency { get; set; } = "VND";
    public bool IsDefault { get; set; }
    public bool IsArchived { get; set; }
    public DateTime? ArchivedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class ExcludedWalletDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public decimal Balance { get; set; }
}

public class TotalAssetsResponse
{
    public decimal TotalAssetsVnd { get; set; }
    public bool RatesUnavailable { get; set; }
    public List<ExcludedWalletDto> ExcludedWallets { get; set; } = new();
}

public class TransferRequest
{
    [Required(ErrorMessage = "Ví nguồn không được để trống")]
    public Guid FromWalletId { get; set; }

    [Required(ErrorMessage = "Ví đích không được để trống")]
    public Guid ToWalletId { get; set; }

    [Range(0.01, double.MaxValue, ErrorMessage = "Số tiền chuyển phải lớn hơn 0")]
    public decimal Amount { get; set; }

    [Range(0, double.MaxValue, ErrorMessage = "Phí chuyển tiền không được âm")]
    public decimal Fee { get; set; } = 0;

    [StringLength(500, ErrorMessage = "Ghi chú tối đa 500 ký tự")]
    public string? Note { get; set; }
}

public class TransferResponse
{
    public Guid TransferId { get; set; }
    public decimal NewSourceBalance { get; set; }
    public decimal NewTargetBalance { get; set; }
}

