using Microsoft.Extensions.Logging.Abstractions;
using RecurringDebtService.Models;
using RecurringDebtService.Services;
using Xunit;

namespace RecurringDebtService.Tests;

public class SchedulerEngineTests
{
    private class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;
        public FakeTimeProvider(DateTimeOffset utcNow) => _utcNow = utcNow;
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public void SetUtcNow(DateTimeOffset utcNow) => _utcNow = utcNow;
    }

    private readonly FakeTimeProvider _timeProvider;
    private readonly SchedulerEngine _engine;

    public SchedulerEngineTests()
    {
        _timeProvider = new FakeTimeProvider(new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero));
        _engine = new SchedulerEngine(null!, _timeProvider, NullLogger<SchedulerEngine>.Instance, null);
    }

    [Fact]
    public void Test_SC01_Monthly_AnchorDay_31_Sequence()
    {
        // Monthly neo 31: chuỗi kỳ từ 31/01/2027: 31/01, 28/02, 31/03, 30/04, 31/05… (không trôi về 28)
        var start = new DateOnly(2027, 1, 31);
        var anchorDay = 31;

        var d1 = _engine.ComputeNextOccurrence(start, FrequencyType.Monthly, anchorDay, start);
        Assert.Equal(new DateOnly(2027, 2, 28), d1);

        var d2 = _engine.ComputeNextOccurrence(d1, FrequencyType.Monthly, anchorDay, start);
        Assert.Equal(new DateOnly(2027, 3, 31), d2);

        var d3 = _engine.ComputeNextOccurrence(d2, FrequencyType.Monthly, anchorDay, start);
        Assert.Equal(new DateOnly(2027, 4, 30), d3);

        var d4 = _engine.ComputeNextOccurrence(d3, FrequencyType.Monthly, anchorDay, start);
        Assert.Equal(new DateOnly(2027, 5, 31), d4);
    }

    [Fact]
    public void Test_SC02_LeapYear_AnchorDay_31_February()
    {
        // Năm nhuận 2028: neo 31/01/2028 -> 29/02/2028
        var start = new DateOnly(2028, 1, 31);
        var anchorDay = 31;

        var d1 = _engine.ComputeNextOccurrence(start, FrequencyType.Monthly, anchorDay, start);
        Assert.Equal(new DateOnly(2028, 2, 29), d1);

        var d2 = _engine.ComputeNextOccurrence(d1, FrequencyType.Monthly, anchorDay, start);
        Assert.Equal(new DateOnly(2028, 3, 31), d2);
    }

    [Fact]
    public void Test_SC03_Yearly_LeapDay_Sequence()
    {
        // Yearly 29/02/2028: 28/02/2029, 28/02/2030, 28/02/2031, 29/02/2032
        var start = new DateOnly(2028, 2, 29);

        var y1 = _engine.ComputeNextOccurrence(start, FrequencyType.Yearly, 29, start);
        Assert.Equal(new DateOnly(2029, 2, 28), y1);

        var y2 = _engine.ComputeNextOccurrence(y1, FrequencyType.Yearly, 29, start);
        Assert.Equal(new DateOnly(2030, 2, 28), y2);

        var y3 = _engine.ComputeNextOccurrence(y2, FrequencyType.Yearly, 29, start);
        Assert.Equal(new DateOnly(2031, 2, 28), y3);

        var y4 = _engine.ComputeNextOccurrence(y3, FrequencyType.Yearly, 29, start);
        Assert.Equal(new DateOnly(2032, 2, 29), y4);
    }

    [Fact]
    public void Test_SC04_Daily_And_Weekly_Calculations()
    {
        var start = new DateOnly(2027, 1, 1);

        var nextDaily = _engine.ComputeNextOccurrence(start, FrequencyType.Daily, 1, start);
        Assert.Equal(new DateOnly(2027, 1, 2), nextDaily);

        var nextWeekly = _engine.ComputeNextOccurrence(start, FrequencyType.Weekly, 1, start);
        Assert.Equal(new DateOnly(2027, 1, 8), nextWeekly);
    }

    [Fact]
    public void Test_SC13_Vietnam_Timezone_Cutoff()
    {
        // Mốc giờ: quét lúc 2027-01-30T17:10Z (00:10 ngày 31/01 giờ Việt Nam) -> Kỳ 31/01 được tính là hôm nay (D31)
        _timeProvider.SetUtcNow(new DateTimeOffset(2027, 1, 30, 17, 10, 0, TimeSpan.Zero));
        var todayVn = _engine.GetTodayVietnam();
        Assert.Equal(new DateOnly(2027, 1, 31), todayVn);
    }

    [Fact]
    public void Test_RR04_No_Retroactive_Creation_For_Past_StartDate()
    {
        // Tạo rule với StartDate = 3 tháng trước (10/10/2026), ngày neo 10. Hôm nay là 24/01/2027
        // Không sinh quá khứ. nextExecutionDate = ngày 10 gần nhất >= hôm nay -> 10/02/2027
        var pastStart = new DateOnly(2026, 10, 10);
        var today = new DateOnly(2027, 1, 24);
        var anchorDay = 10;

        var next = _engine.FindInitialNextExecutionDate(pastStart, FrequencyType.Monthly, anchorDay, today);
        Assert.Equal(new DateOnly(2027, 2, 10), next);
    }
}

