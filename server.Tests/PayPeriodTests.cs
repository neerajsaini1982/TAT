using Server.Models;

namespace Server.Tests;

public class PayPeriodTests
{
    // Pay day Fri 10/09/2026 pays for Sun 09/20 – Sat 10/03, every 2 weeks.
    private static LocationSettings Biweekly() => new()
    {
        PayDayStartDate = new DateOnly(2026, 10, 9),
        PayPeriodDays = 14,
        PayPeriodStartDate = new DateOnly(2026, 9, 20),
    };

    [Fact]
    public void PayPeriod_ForTheConfiguredPayDay_IsTheConfiguredPeriod()
    {
        var period = Biweekly().GetPayPeriodFor(new DateOnly(2026, 10, 9));

        Assert.Equal((new DateOnly(2026, 9, 20), new DateOnly(2026, 10, 3)), period);
    }

    [Theory]
    [InlineData("2026-10-23", "2026-10-04", "2026-10-17")]
    [InlineData("2026-09-25", "2026-09-06", "2026-09-19")]
    public void PayPeriod_ForAnotherPayDay_ShiftsByTheSameNumberOfDays(string payDate, string start, string end)
    {
        var period = Biweekly().GetPayPeriodFor(DateOnly.Parse(payDate));

        Assert.Equal((DateOnly.Parse(start), DateOnly.Parse(end)), period);
    }

    [Fact]
    public void PayPeriod_FollowsTheNextPayDate()
    {
        var settings = Biweekly();

        var nextPayDate = settings.GetNextPayDate(new DateOnly(2026, 10, 10));

        Assert.Equal(new DateOnly(2026, 10, 23), nextPayDate);
        Assert.Equal((new DateOnly(2026, 10, 4), new DateOnly(2026, 10, 17)), settings.GetPayPeriodFor(nextPayDate));
    }

    [Fact]
    public void PayPeriod_WithoutAStartDate_IsNull()
    {
        var settings = Biweekly();
        settings.PayPeriodStartDate = null;

        Assert.Null(settings.GetPayPeriodFor(new DateOnly(2026, 10, 9)));
    }
}
