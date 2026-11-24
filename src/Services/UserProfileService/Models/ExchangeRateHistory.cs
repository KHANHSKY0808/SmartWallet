namespace UserProfileService.Models;

public class ExchangeRateHistory
{
    public int Id { get; set; }
    public string BaseCurrency { get; set; } = string.Empty;
    public string TargetCurrency { get; set; } = string.Empty;
    public decimal OldRate { get; set; }
    public decimal NewRate { get; set; }
    public Guid? UpdatedBy { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
