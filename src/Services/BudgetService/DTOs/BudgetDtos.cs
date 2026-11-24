using System.ComponentModel.DataAnnotations;

namespace BudgetService.DTOs;

public class CreateBudgetRequest
{
    [Required(ErrorMessage = "CategoryId là bắt buộc")]
    public Guid CategoryId { get; set; }

    [Required(ErrorMessage = "Amount là bắt buộc")]
    [Range(0.01, 1000000000000.0, ErrorMessage = "Số tiền ngân sách phải lớn hơn 0 và tối đa 1.000 tỷ")]
    public decimal Amount { get; set; }

    [Required(ErrorMessage = "Month là bắt buộc")]
    [Range(1, 12, ErrorMessage = "Tháng phải trong khoảng từ 1 đến 12")]
    public int Month { get; set; }

    [Required(ErrorMessage = "Year là bắt buộc")]
    [Range(2000, 2100, ErrorMessage = "Năm phải trong khoảng từ 2000 đến 2100")]
    public int Year { get; set; }
}

public class UpdateBudgetRequest
{
    [Required(ErrorMessage = "Amount là bắt buộc")]
    [Range(0.01, 1000000000000.0, ErrorMessage = "Số tiền ngân sách phải lớn hơn 0 và tối đa 1.000 tỷ")]
    public decimal Amount { get; set; }
}

public class BudgetDto
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid CategoryId { get; set; }
    public decimal Amount { get; set; }
    public int Month { get; set; }
    public int Year { get; set; }
    public decimal CurrentSpent { get; set; }
    public decimal Percent { get; set; }
    public string Status { get; set; } = "normal"; // normal (<80), warning (80-99), exceeded (>=100)
    public bool IsAlertSent80 { get; set; }
    public bool IsAlertSent100 { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class BudgetFilterParameters
{
    public int? Month { get; set; }
    public int? Year { get; set; }
}

