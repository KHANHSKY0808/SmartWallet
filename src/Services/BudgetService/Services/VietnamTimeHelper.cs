namespace BudgetService.Services;

public static class VietnamTimeHelper
{
    private static readonly TimeZoneInfo VietnamTimeZone = GetTimeZone();

    private static TimeZoneInfo GetTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
        }
        catch (TimeZoneNotFoundException)
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");
            }
            catch (Exception)
            {
                return TimeZoneInfo.CreateCustomTimeZone("UTC+7", TimeSpan.FromHours(7), "UTC+7", "UTC+7");
            }
        }
    }

    /// <summary>
    /// Chuyển đổi DateTime UTC sang giờ Việt Nam (UTC+7)
    /// </summary>
    public static DateTime ToVietnamTime(DateTime utcDateTime)
    {
        var utc = utcDateTime.Kind == DateTimeKind.Utc 
            ? utcDateTime 
            : DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTimeFromUtc(utc, VietnamTimeZone);
    }

    /// <summary>
    /// Lấy (Month, Year) theo giờ Việt Nam từ DateTime UTC (D31)
    /// </summary>
    public static (int Month, int Year) GetMonthYearInVietnam(DateTime utcDateTime)
    {
        var vn = ToVietnamTime(utcDateTime);
        return (vn.Month, vn.Year);
    }

    /// <summary>
    /// Lấy tháng và năm hiện tại theo giờ Việt Nam (D31)
    /// </summary>
    public static (int Month, int Year) GetCurrentMonthYearInVietnam()
    {
        return GetMonthYearInVietnam(DateTime.UtcNow);
    }

    /// <summary>
    /// Tính khoảng thời gian UTC bắt đầu và kết thúc của một tháng theo giờ Việt Nam (D31)
    /// </summary>
    public static (DateTime StartUtc, DateTime EndUtc) GetMonthDateRangeInUtc(int month, int year)
    {
        // 00:00:00 ngày đầu tháng giờ VN
        var startVn = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Unspecified);
        // 23:59:59.999 ngày cuối tháng giờ VN
        var endVn = startVn.AddMonths(1).AddTicks(-1);

        var startUtc = TimeZoneInfo.ConvertTimeToUtc(startVn, VietnamTimeZone);
        var endUtc = TimeZoneInfo.ConvertTimeToUtc(endVn, VietnamTimeZone);
        return (startUtc, endUtc);
    }
}

