using Server.Models;
using Server.Services;

namespace Server.Tests;

public class OvertimeCalculatorTests
{
    // 2026-09-14 is a Monday, so Mon..Sun are offsets 0..6 and the next
    // workweek starts at offset 7.
    private static readonly DateOnly Monday = new(2026, 9, 14);

    private static DayHours H(int dayOffset, double hours) =>
        new(Monday.AddDays(dayOffset), (int)Math.Round(hours * 60));

    private static (int Regular, int Overtime, int DoubleTime) Totals(IEnumerable<DayPay> days) =>
        (days.Sum(d => d.RegularMinutes), days.Sum(d => d.OvertimeMinutes), days.Sum(d => d.DoubleTimeMinutes));

    private static DayPay Day(IReadOnlyList<DayPay> result, int dayOffset) =>
        result.Single(d => d.Date == Monday.AddDays(dayOffset));

    private static void AssertDay(DayPay day, double regularHours, double overtimeHours, double doubleTimeHours)
    {
        Assert.Equal((int)(regularHours * 60), day.RegularMinutes);
        Assert.Equal((int)(overtimeHours * 60), day.OvertimeMinutes);
        Assert.Equal((int)(doubleTimeHours * 60), day.DoubleTimeMinutes);
    }

    private static readonly OvertimePolicy California = OvertimePolicy.ForPreset(OvertimePreset.California);
    private static readonly OvertimePolicy Federal = OvertimePolicy.ForPreset(OvertimePreset.Federal);

    // What every location had before overtime rules became configurable:
    // overtime past 8 hours in a day, nothing else.
    private static readonly OvertimePolicy DailyOnly = new LocationSettings().GetOvertimePolicy();

    [Fact]
    public void No_input_gives_no_output()
    {
        Assert.Empty(OvertimeCalculator.Calculate(California, []));
    }

    [Fact]
    public void None_policy_pays_everything_as_regular()
    {
        var result = OvertimeCalculator.Calculate(OvertimePolicy.None, [H(0, 14), H(1, 9), H(2, 9), H(3, 9), H(4, 9), H(5, 9), H(6, 9)]);

        Assert.Equal((68 * 60, 0, 0), Totals(result));
    }

    [Fact]
    public void Default_location_policy_reproduces_the_original_daily_rule()
    {
        var result = OvertimeCalculator.Calculate(DailyOnly, [H(0, 10), H(1, 8), H(2, 7.5), H(3, 12)]);

        AssertDay(Day(result, 0), 8, 2, 0);
        AssertDay(Day(result, 1), 8, 0, 0);
        AssertDay(Day(result, 2), 7.5, 0, 0);
        // No double-time and no weekly rule: everything past 8h is plain overtime.
        AssertDay(Day(result, 3), 8, 4, 0);
    }

    [Fact]
    public void Exactly_at_a_threshold_is_not_overtime()
    {
        var result = OvertimeCalculator.Calculate(California, [H(0, 8), H(1, 12)]);

        AssertDay(Day(result, 0), 8, 0, 0);
        AssertDay(Day(result, 1), 8, 4, 0);
    }

    [Fact]
    public void Federal_weekly_overtime_starts_on_the_day_that_crosses_forty_hours()
    {
        // 4 x 9h = 36h, so only 4h of Friday's 9h is still regular.
        var result = OvertimeCalculator.Calculate(Federal, [H(0, 9), H(1, 9), H(2, 9), H(3, 9), H(4, 9)]);

        AssertDay(Day(result, 3), 9, 0, 0);
        AssertDay(Day(result, 4), 4, 5, 0);
        Assert.Equal((40 * 60, 5 * 60, 0), Totals(result));
    }

    [Fact]
    public void Federal_has_no_daily_overtime()
    {
        var result = OvertimeCalculator.Calculate(Federal, [H(0, 14)]);

        AssertDay(Day(result, 0), 14, 0, 0);
    }

    [Fact]
    public void Weekly_hours_reset_at_the_workweek_boundary()
    {
        // 45h in the first week, 30h in the second: only the first week has overtime.
        var result = OvertimeCalculator.Calculate(Federal,
            [H(0, 9), H(1, 9), H(2, 9), H(3, 9), H(4, 9), H(7, 10), H(8, 10), H(9, 10)]);

        Assert.Equal(5 * 60, result.Where(d => d.Date < Monday.AddDays(7)).Sum(d => d.OvertimeMinutes));
        Assert.Equal(0, result.Where(d => d.Date >= Monday.AddDays(7)).Sum(d => d.OvertimeMinutes));
    }

    [Fact]
    public void Workweek_start_day_moves_the_boundary()
    {
        // Sat/Sun/Mon are one workweek when the week starts on Saturday, so
        // 3 x 15h = 45h is overtime; with a Monday start Sat/Sun and Mon are
        // separate weeks and nothing crosses 40h.
        var days = new[] { H(5, 15), H(6, 15), H(7, 15) };

        var saturdayStart = Federal with { WorkweekStartDay = DayOfWeek.Saturday };
        Assert.Equal((40 * 60, 5 * 60, 0), Totals(OvertimeCalculator.Calculate(saturdayStart, days)));
        Assert.Equal((45 * 60, 0, 0), Totals(OvertimeCalculator.Calculate(Federal, days)));
    }

    [Fact]
    public void California_worked_example_from_the_design_notes()
    {
        var result = OvertimeCalculator.Calculate(California, [H(0, 10), H(1, 13), H(2, 8), H(3, 8), H(4, 8), H(5, 8)]);

        AssertDay(Day(result, 0), 8, 2, 0);
        AssertDay(Day(result, 1), 8, 4, 1);
        AssertDay(Day(result, 2), 8, 0, 0);
        AssertDay(Day(result, 3), 8, 0, 0);
        AssertDay(Day(result, 4), 8, 0, 0);
        // Regular time is already at 40h, so all of Saturday is weekly overtime.
        AssertDay(Day(result, 5), 0, 8, 0);
        Assert.Equal((40 * 60, 14 * 60, 1 * 60), Totals(result));
    }

    [Fact]
    public void California_hours_already_paid_as_daily_overtime_do_not_count_toward_the_weekly_forty()
    {
        // 4 x 10h: 32h regular + 8h daily overtime. Only 32h of regular time
        // has accrued, so Friday's first 8h are still regular (reaching 40).
        var result = OvertimeCalculator.Calculate(California, [H(0, 10), H(1, 10), H(2, 10), H(3, 10), H(4, 10)]);

        AssertDay(Day(result, 3), 8, 2, 0);
        AssertDay(Day(result, 4), 8, 2, 0);
        Assert.Equal((40 * 60, 10 * 60, 0), Totals(result));
    }

    [Fact]
    public void California_seventh_consecutive_day_is_overtime_then_double_time_after_eight_hours()
    {
        var result = OvertimeCalculator.Calculate(California,
            [H(0, 8), H(1, 8), H(2, 8), H(3, 8), H(4, 8), H(5, 8), H(6, 10)]);

        AssertDay(Day(result, 5), 0, 8, 0); // weekly overtime
        AssertDay(Day(result, 6), 0, 8, 2); // seventh day
    }

    [Fact]
    public void Seventh_day_hours_never_count_toward_weekly_regular_time()
    {
        // 6 x 4h = 24h regular; the 7th day is all premium and leaves the
        // regular total at 24h.
        var result = OvertimeCalculator.Calculate(California,
            [H(0, 4), H(1, 4), H(2, 4), H(3, 4), H(4, 4), H(5, 4), H(6, 9)]);

        AssertDay(Day(result, 6), 0, 8, 1);
        Assert.Equal((24 * 60, 8 * 60, 1 * 60), Totals(result));
    }

    [Fact]
    public void Seventh_day_rule_needs_every_earlier_day_of_the_week_worked()
    {
        // Wednesday off, so Sunday is not a seventh consecutive day.
        var result = OvertimeCalculator.Calculate(California,
            [H(0, 6), H(1, 6), H(3, 6), H(4, 6), H(5, 6), H(6, 6)]);

        AssertDay(Day(result, 6), 6, 0, 0);
    }

    [Fact]
    public void A_zero_minute_day_does_not_count_as_worked_for_the_seventh_day_rule()
    {
        var result = OvertimeCalculator.Calculate(California,
            [H(0, 6), H(1, 6), H(2, 0), H(3, 6), H(4, 6), H(5, 6), H(6, 6)]);

        AssertDay(Day(result, 6), 6, 0, 0);
    }

    [Fact]
    public void Seventh_day_rule_is_off_when_the_policy_leaves_it_null()
    {
        var policy = California with { SeventhDayDoubleTimeAfterMinutes = null };
        var result = OvertimeCalculator.Calculate(policy,
            [H(0, 4), H(1, 4), H(2, 4), H(3, 4), H(4, 4), H(5, 4), H(6, 6)]);

        AssertDay(Day(result, 6), 6, 0, 0);
    }

    [Fact]
    public void A_seventh_day_only_counts_within_its_own_workweek()
    {
        // Seven straight days, but the run spans two Monday-start workweeks
        // (Wed..Sun then Mon..Tue), so neither week has a seventh day.
        var result = OvertimeCalculator.Calculate(California,
            [H(2, 4), H(3, 4), H(4, 4), H(5, 4), H(6, 4), H(7, 4), H(8, 4)]);

        Assert.Equal((28 * 60, 0, 0), Totals(result));
    }

    [Fact]
    public void A_double_time_only_threshold_also_starts_overtime_there()
    {
        var policy = OvertimePolicy.None with { DailyDoubleTimeAfterMinutes = 12 * 60 };
        var result = OvertimeCalculator.Calculate(policy, [H(0, 14)]);

        AssertDay(Day(result, 0), 12, 0, 2);
    }

    [Fact]
    public void Repeated_dates_are_summed_before_the_rules_apply()
    {
        // A split shift: 5h + 6h on one day is 11h.
        var result = OvertimeCalculator.Calculate(California, [H(0, 5), H(0, 6)]);

        var day = Assert.Single(result);
        AssertDay(day, 8, 3, 0);
    }

    [Fact]
    public void Negative_minutes_are_treated_as_zero()
    {
        var result = OvertimeCalculator.Calculate(California, [new DayHours(Monday, -30)]);

        AssertDay(Assert.Single(result), 0, 0, 0);
    }

    [Fact]
    public void Results_come_back_in_date_order_regardless_of_input_order()
    {
        var result = OvertimeCalculator.Calculate(California, [H(9, 1), H(0, 1), H(7, 1), H(2, 1)]);

        Assert.Equal(result.Select(d => d.Date).Order(), result.Select(d => d.Date));
    }

    [Theory]
    [InlineData(OvertimePreset.None)]
    [InlineData(OvertimePreset.Federal)]
    [InlineData(OvertimePreset.California)]
    public void Every_minute_is_accounted_for_and_no_bucket_goes_negative(OvertimePreset preset)
    {
        var policy = OvertimePolicy.ForPreset(preset);
        var random = new Random(42);
        var input = Enumerable.Range(0, 8 * 7)
            .Where(_ => random.Next(10) < 8)
            .Select(offset => new DayHours(Monday.AddDays(offset), random.Next(0, 16 * 60)))
            .ToList();

        var result = OvertimeCalculator.Calculate(policy, input);

        Assert.Equal(input.Count, result.Count);
        foreach (var day in result)
        {
            Assert.True(day.RegularMinutes >= 0 && day.OvertimeMinutes >= 0 && day.DoubleTimeMinutes >= 0);
            Assert.Equal(
                input.Single(i => i.Date == day.Date).NetMinutes,
                day.RegularMinutes + day.OvertimeMinutes + day.DoubleTimeMinutes);
        }
    }

    [Fact]
    public void Presets_hold_the_expected_rules()
    {
        Assert.Equal(new OvertimePolicy(null, null, 2400, null, DayOfWeek.Monday), Federal);
        Assert.Equal(new OvertimePolicy(480, 720, 2400, 480, DayOfWeek.Monday), California);
        Assert.Equal(OvertimePolicy.None, OvertimePolicy.ForPreset(OvertimePreset.None));
    }

    [Fact]
    public void A_new_location_keeps_the_original_behavior()
    {
        var settings = new LocationSettings();

        Assert.Equal(OvertimePreset.Custom, settings.OvertimePreset);
        Assert.Equal(new OvertimePolicy(480, null, null, null, DayOfWeek.Monday), settings.GetOvertimePolicy());
    }
}
