namespace Server.Models;

public enum TimeFormat
{
    TwelveHour,
    TwentyFourHour,
}

public enum DateFormat
{
    MmDdYyyy, // 07/10/2026
    DdMmYyyy, // 10/07/2026
    YyyyMmDd, // 2026-07-10
    DdMmmYyyy, // 10-Jul-2026
    MmmDdYyyy, // Jul 10, 2026
}

// One row per Location, created on first access with sensible defaults.
public class LocationSettings
{
    public int Id { get; set; }

    public int LocationId { get; set; }
    public Location? Location { get; set; }

    public TimeFormat TimeFormat { get; set; } = TimeFormat.TwelveHour;
    public DateFormat DateFormat { get; set; } = DateFormat.MmDdYyyy;

    // IANA time zone id, e.g. "America/Los_Angeles".
    public string TimeZone { get; set; } = "America/Los_Angeles";

    // How many days ahead of today employees are asked to plan
    // availability for (drives reminder copy; the submission-window rule
    // itself is fixed to "next week" — see AvailabilityController).
    public int AvailabilityDays { get; set; } = 7;

    // How many minutes before a shift's scheduled start time the Clock In
    // button becomes enabled for that shift (see TimeEntriesController).
    public int ClockInWindowMinutes { get; set; } = 15;

    // Attendance thresholds used to auto-flag (not block) punches that run
    // past what's expected — see TimeEntriesController/attendance-flags.ts.
    // A clock-in more than this many minutes after the shift's scheduled
    // start is flagged late.
    public int LateClockInGraceMinutes { get; set; } = 5;
    // A Break (or second Break) longer than this many minutes is flagged.
    public int BreakLimitMinutes { get; set; } = 15;
    // A Lunch longer than this many minutes is flagged.
    public int LunchLimitMinutes { get; set; } = 30;
    // Net worked time beyond this many minutes in a single day counts as
    // overtime on the hours report (see ReportsController). Defaults to 480
    // (8 hours).
    public int OvertimeDailyThresholdMinutes { get; set; } = 480;

    // When on, exposes extra diagnostics/test affordances for this location.
    public bool DevelopmentMode { get; set; } = false;

    // Governs whether roles other than Admin can see everyone's shifts (not
    // just their own) on the "My Schedule" page — the admin roster/management
    // page always shows everyone regardless of this. When disabled, every
    // role only ever sees their own shifts there, ignoring the three flags
    // below entirely.
    public bool ScheduleVisibilityEnabled { get; set; } = true;
    public bool AdminSeesAllSchedules { get; set; } = true;
    public bool LeadSeesAllSchedules { get; set; } = false;
    public bool EmployeeSeesAllSchedules { get; set; } = false;

    public string? SmtpHost { get; set; }
    public int? SmtpPort { get; set; }
    public string? SmtpUsername { get; set; }
    public string? SmtpPassword { get; set; }
    public bool SmtpUseSsl { get; set; } = true;
    public string? SmtpFromAddress { get; set; }
    public string? SmtpFromName { get; set; }

    // A single known pay day plus the recurrence interval, from which every
    // future pay date is derived on read (see GetNextPayDate) — avoids
    // needing an ever-growing list of pay day entries. Null means pay day
    // tracking isn't configured for this location.
    public DateOnly? PayDayStartDate { get; set; }
    public int? PayPeriodDays { get; set; }

    // Next pay date on/after `today`, walking forward from PayDayStartDate
    // in PayPeriodDays-sized steps. Returns `today` itself when today is a
    // pay day.
    public DateOnly? GetNextPayDate(DateOnly today)
    {
        if (PayDayStartDate is not { } start || PayPeriodDays is not { } period || period <= 0)
        {
            return null;
        }
        if (start > today)
        {
            return start;
        }
        var elapsedDays = today.DayNumber - start.DayNumber;
        var remainder = elapsedDays % period;
        return remainder == 0 ? today : today.AddDays(period - remainder);
    }
}
