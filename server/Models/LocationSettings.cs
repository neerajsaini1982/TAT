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

    // When on, exposes extra diagnostics/test affordances for this location.
    public bool DevelopmentMode { get; set; } = false;

    public string? SmtpHost { get; set; }
    public int? SmtpPort { get; set; }
    public string? SmtpUsername { get; set; }
    public string? SmtpPassword { get; set; }
    public bool SmtpUseSsl { get; set; } = true;
    public string? SmtpFromAddress { get; set; }
    public string? SmtpFromName { get; set; }
}
