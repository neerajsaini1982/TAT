namespace Server.Models;

// Convenience starting points for a location's overtime rules. The rule
// fields on LocationSettings hold the resolved values — the preset is only
// recorded so the settings UI can show which one was picked; Custom means the
// fields were set by hand (and is what pre-existing locations were migrated
// to, since they only ever had a daily threshold).
public enum OvertimePreset
{
    None, // no premium pay: every worked minute is regular
    Federal, // FLSA: time-and-a-half past 40 hours in the workweek
    California, // daily 8/12, weekly 40, and the seventh-consecutive-day rule
    Custom,
}

// The rules the OvertimeCalculator applies — every threshold is nullable
// with null meaning "this rule doesn't apply", so a location can turn on any
// combination of them. All thresholds are minutes.
//
// Thresholds are "after" values: time up to and including the threshold is
// paid at the lower rate, so DailyOvertimeAfterMinutes = 480 means the first
// 8 hours of a day are regular.
public sealed record OvertimePolicy(
    // Net minutes in one day beyond which time is overtime (1.5x).
    int? DailyOvertimeAfterMinutes,
    // Net minutes in one day beyond which time is double-time (2x).
    int? DailyDoubleTimeAfterMinutes,
    // Regular minutes in one workweek beyond which time is overtime. Only
    // regular time counts toward this — minutes already paid as daily
    // overtime/double-time are never counted a second time.
    int? WeeklyOvertimeAfterMinutes,
    // When the employee works every day of the workweek (so the last day is
    // their seventh consecutive), all time that day is at least overtime,
    // and time beyond this many minutes is double-time. Null turns the rule
    // off.
    int? SeventhDayDoubleTimeAfterMinutes,
    // First day of the workweek — the weekly and seventh-day rules are
    // measured within it. A fixed 7-day cycle the employer chooses.
    DayOfWeek WorkweekStartDay)
{
    public static OvertimePolicy None { get; } = new(null, null, null, null, DayOfWeek.Monday);

    public static OvertimePolicy ForPreset(OvertimePreset preset) => preset switch
    {
        OvertimePreset.Federal => new(null, null, 40 * 60, null, DayOfWeek.Monday),
        OvertimePreset.California => new(8 * 60, 12 * 60, 40 * 60, 8 * 60, DayOfWeek.Monday),
        // Custom has no canonical values of its own — the settings row holds
        // them — so it falls back to no premium pay like None.
        _ => None,
    };

    // Whether these rules are exactly the given preset's. The workweek start
    // day is ignored: it belongs to the location, not to a preset, so a
    // Sunday-start location can still be "California".
    public bool MatchesPreset(OvertimePreset preset) =>
        preset != OvertimePreset.Custom
        && this with { WorkweekStartDay = DayOfWeek.Monday } == ForPreset(preset) with { WorkweekStartDay = DayOfWeek.Monday };
}
