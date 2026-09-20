using Server.Models;

namespace Server.Dtos;

public record LocationSettingsDto(
    TimeFormat TimeFormat,
    DateFormat DateFormat,
    string TimeZone,
    int AvailabilityDays,
    int ClockInWindowMinutes,
    int LateClockInGraceMinutes,
    int BreakLimitMinutes,
    int LunchLimitMinutes,
    OvertimePreset OvertimePreset,
    int? OvertimeDailyThresholdMinutes,
    int? DailyDoubleTimeAfterMinutes,
    int? WeeklyOvertimeAfterMinutes,
    int? SeventhDayDoubleTimeAfterMinutes,
    DayOfWeek WorkweekStartDay,
    bool DevelopmentMode,
    bool ScheduleVisibilityEnabled,
    bool AdminSeesAllSchedules,
    bool LeadSeesAllSchedules,
    bool EmployeeSeesAllSchedules,
    // When off, self-service punches are restricted to an approved IP — see
    // AllowedPunchDeviceDto/AllowedPunchDevicesController.
    bool ClockInAnywhere,
    string? SmtpHost,
    int? SmtpPort,
    string? SmtpUsername,
    bool SmtpUseSsl,
    string? SmtpFromAddress,
    string? SmtpFromName,
    // Never round-trips the stored password; true only tells the UI one is
    // already on file so it can show a placeholder instead of a blank box.
    bool HasSmtpPassword,
    DateOnly? PayDayStartDate,
    int? PayPeriodDays,
    // Computed from PayDayStartDate/PayPeriodDays (see LocationSettings.GetNextPayDate) — not stored.
    DateOnly? NextPayDate,
    // Never round-trips the stored passcode; true only tells the UI a kiosk
    // device can log in for this location, same HasSmtpPassword pattern.
    bool HasKioskPasscode);

public record UpdateLocationSettingsRequest(
    TimeFormat TimeFormat,
    DateFormat DateFormat,
    string TimeZone,
    int AvailabilityDays,
    int ClockInWindowMinutes,
    int LateClockInGraceMinutes,
    int BreakLimitMinutes,
    int LunchLimitMinutes,
    OvertimePreset OvertimePreset,
    int? OvertimeDailyThresholdMinutes,
    int? DailyDoubleTimeAfterMinutes,
    int? WeeklyOvertimeAfterMinutes,
    int? SeventhDayDoubleTimeAfterMinutes,
    DayOfWeek WorkweekStartDay,
    bool DevelopmentMode,
    bool ScheduleVisibilityEnabled,
    bool AdminSeesAllSchedules,
    bool LeadSeesAllSchedules,
    bool EmployeeSeesAllSchedules,
    bool ClockInAnywhere,
    string? SmtpHost,
    int? SmtpPort,
    string? SmtpUsername,
    // Blank/omitted leaves the existing stored password untouched, since
    // GET never sends the real value back down for the field to round-trip.
    string? SmtpPassword,
    bool SmtpUseSsl,
    string? SmtpFromAddress,
    string? SmtpFromName,
    DateOnly? PayDayStartDate,
    int? PayPeriodDays,
    // Blank/omitted leaves the existing stored passcode untouched, same
    // "blank means unchanged" rule SmtpPassword uses.
    string? KioskPasscode,
    // Explicitly clears the stored passcode (disables kiosk login for this
    // location) — a blank KioskPasscode alone can't mean "clear it" because
    // blank also means "unchanged".
    bool ClearKioskPasscode);

// Lets an admin verify SMTP settings actually work before (or after) saving
// them. SmtpHost/Username/etc mirror whatever is currently in the form —
// not necessarily what's saved yet — so testing doesn't require a save
// round-trip first. A blank SmtpPassword falls back to the saved one, same
// "blank means unchanged" rule UpdateLocationSettingsRequest uses.
public record SendTestEmailRequest(
    string ToAddress,
    string? SmtpHost,
    int? SmtpPort,
    string? SmtpUsername,
    string? SmtpPassword,
    bool SmtpUseSsl,
    string? SmtpFromAddress,
    string? SmtpFromName);

// Minimal subset any signed-in account (not just Admin/Sa) can read, so an
// Employee's client can compute when its own Clock In buttons unlock,
// whether its own punches should render as late/over-limit, and how to
// display every scheduled/punch time (TimeFormat + TimeZone) consistently
// with what the admin configured for this location.
public record EmployeeLocationSettingsDto(
    TimeFormat TimeFormat,
    string TimeZone,
    int ClockInWindowMinutes,
    int LateClockInGraceMinutes,
    int BreakLimitMinutes,
    int LunchLimitMinutes,
    // Computed from LocationSettings.GetNextPayDate; null when pay day tracking isn't configured.
    DateOnly? NextPayDate);

// One selectable overtime preset with the rule values it fills in (see
// OvertimePolicy.ForPreset). The workweek start day isn't part of a preset —
// it's a property of the location, so choosing a preset leaves it alone.
public record OvertimePresetDto(
    OvertimePreset Preset,
    int? OvertimeDailyThresholdMinutes,
    int? DailyDoubleTimeAfterMinutes,
    int? WeeklyOvertimeAfterMinutes,
    int? SeventhDayDoubleTimeAfterMinutes);
