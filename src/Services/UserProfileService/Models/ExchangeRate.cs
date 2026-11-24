namespace UserProfileService.Models;

public class ExchangeRate
{
    public int Id { get; set; }
    public string BaseCurrency { get; set; } = string.Empty;
    public string TargetCurrency { get; set; } = string.Empty;
    public decimal Rate { get; set; }
    public string Source { get; set; } = "Manual";
    public Guid? UpdatedBy { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
