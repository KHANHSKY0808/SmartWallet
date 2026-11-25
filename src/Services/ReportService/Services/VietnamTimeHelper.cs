namespace ReportService.Services;

public static class VietnamTimeHelper
{
    public static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    public static DateTime ToVietnamTime(DateTime utcOrUnspecified)
    {
        var utc = utcOrUnspecified.Kind == DateTimeKind.Utc
            ? utcOrUnspecified
            : DateTime.SpecifyKind(utcOrUnspecified, DateTimeKind.Utc);
        return utc.Add(VietnamOffset);
    }

    public static DateOnly ToVietnamDate(DateTime utcOrUnspecified)
    {
        var vn = ToVietnamTime(utcOrUnspecified);
        return DateOnly.FromDateTime(vn);
    }

    public static (int Month, int Year) GetCurrentMonthYearInVietnam(TimeProvider? timeProvider = null)
    {
        var utcNow = (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        var vn = utcNow.Add(VietnamOffset);
        return (vn.Month, vn.Year);
    }

    /// <summary>
    /// Chuyển kỳ tháng/năm theo giờ VN thành khoảng UTC [startUtc, endUtcExclusive) để truy vấn có index.
    /// </summary>
    public static (DateTime StartUtc, DateTime EndUtcExclusive) GetMonthRangeUtc(int month, int year)
    {
        var startVn = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Unspecified);
        var endVn = startVn.AddMonths(1);
        return (
            DateTime.SpecifyKind(startVn - VietnamOffset, DateTimeKind.Utc),
            DateTime.SpecifyKind(endVn - VietnamOffset, DateTimeKind.Utc)
        );
    }

    /// <summary>
    /// Chuyển khoảng [from, to] (inclusive) theo giờ VN thành khoảng UTC [startUtc, endUtcExclusive).
    /// </summary>
    public static (DateTime StartUtc, DateTime EndUtcExclusive) GetDateRangeUtc(DateOnly from, DateOnly to)
    {
        var startVn = from.ToDateTime(TimeOnly.MinValue);
        var endVn = to.AddDays(1).ToDateTime(TimeOnly.MinValue);
        return (
            DateTime.SpecifyKind(startVn - VietnamOffset, DateTimeKind.Utc),
            DateTime.SpecifyKind(endVn - VietnamOffset, DateTimeKind.Utc)
        );
    }

    /// <summary>
    /// Lấy ngày thứ Hai đầu tuần theo ISO-8601 (AN-04 / D59).
    /// </summary>
    public static DateOnly GetMondayOfWeek(DateOnly date)
    {
        int diff = ((int)date.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        return date.AddDays(-diff);
    }
}

