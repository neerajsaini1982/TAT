using Server.Models;

namespace Server.Services;

// Net worked minutes on one date — what the hours report calls
// NetWorkedMinutes. Sick time isn't worked time and doesn't belong here.
public readonly record struct DayHours(DateOnly Date, int NetMinutes);

// A day's worked minutes split by pay rate. Regular + Overtime + DoubleTime
// always equals the day's input NetMinutes.
public readonly record struct DayPay(DateOnly Date, int RegularMinutes, int OvertimeMinutes, int DoubleTimeMinutes);

// Splits worked time into regular / overtime (1.5x) / double-time (2x)
// according to an OvertimePolicy. Pure and stateless — no database, clock or
// settings access — so the rules can be tested exhaustively on their own.
//
// The weekly and seventh-day rules look at the whole workweek, so the caller
// has to pass every worked day of each workweek it wants an answer for, not
// just the days inside a report's date range; a partial week under-counts
// weekly hours and can miss a seventh day.
public static class OvertimeCalculator
{
    private const int DaysPerWeek = 7;

    // One result per distinct input date, in date order. Repeated dates are
    // summed (a split shift is two entries on one day). Negative minutes are
    // treated as zero.
    public static IReadOnlyList<DayPay> Calculate(OvertimePolicy policy, IEnumerable<DayHours> days)
    {
        var minutesByDate = days
            .GroupBy(d => d.Date)
            .ToDictionary(g => g.Key, g => Math.Max(0, g.Sum(d => d.NetMinutes)));

        var results = new List<DayPay>(minutesByDate.Count);

        foreach (var week in minutesByDate.Keys.GroupBy(d => WorkweekStart(d, policy.WorkweekStartDay)).OrderBy(g => g.Key))
        {
            var weekStart = week.Key;
            var regularSoFar = 0;

            foreach (var date in week.OrderBy(d => d))
            {
                var net = minutesByDate[date];

                var isSeventhDay =
                    policy.SeventhDayDoubleTimeAfterMinutes is not null
                    && date == weekStart.AddDays(DaysPerWeek - 1)
                    && net > 0
                    && Enumerable.Range(0, DaysPerWeek - 1)
                        .All(offset => minutesByDate.TryGetValue(weekStart.AddDays(offset), out var m) && m > 0);

                int regular, overtime, doubleTime;
                if (isSeventhDay)
                {
                    var doubleTimeStart = policy.SeventhDayDoubleTimeAfterMinutes!.Value;
                    regular = 0;
                    overtime = Math.Min(net, doubleTimeStart);
                    doubleTime = net - overtime;
                }
                else
                {
                    (regular, overtime, doubleTime) = SplitByDay(policy, net);
                }

                if (policy.WeeklyOvertimeAfterMinutes is { } weeklyLimit)
                {
                    // Regular time past the weekly limit becomes overtime.
                    // Minutes that are already premium never enter regularSoFar.
                    var room = Math.Max(0, weeklyLimit - regularSoFar);
                    var excess = Math.Max(0, regular - room);
                    regular -= excess;
                    overtime += excess;
                }

                regularSoFar += regular;
                results.Add(new DayPay(date, regular, overtime, doubleTime));
            }
        }

        return results;
    }

    private static (int Regular, int Overtime, int DoubleTime) SplitByDay(OvertimePolicy policy, int net)
    {
        // Double-time can't start before overtime does, so a policy with
        // only a double-time threshold behaves as if overtime starts there too.
        var doubleTimeStart = policy.DailyDoubleTimeAfterMinutes ?? int.MaxValue;
        var overtimeStart = Math.Min(policy.DailyOvertimeAfterMinutes ?? int.MaxValue, doubleTimeStart);

        var doubleTime = Math.Max(0, net - doubleTimeStart);
        var overtime = Math.Max(0, Math.Min(net, doubleTimeStart) - overtimeStart);
        return (net - overtime - doubleTime, overtime, doubleTime);
    }

    // The most recent `startDay` on or before `date`.
    private static DateOnly WorkweekStart(DateOnly date, DayOfWeek startDay)
    {
        var daysSinceStart = ((int)date.DayOfWeek - (int)startDay + DaysPerWeek) % DaysPerWeek;
        return date.AddDays(-daysSinceStart);
    }
}
