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
    bool DevelopmentMode,
    string? SmtpHost,
    int? SmtpPort,
    string? SmtpUsername,
    bool SmtpUseSsl,
    string? SmtpFromAddress,
    string? SmtpFromName,
    // Never round-trips the stored password; true only tells the UI one is
    // already on file so it can show a placeholder instead of a blank box.
    bool HasSmtpPassword);

public record UpdateLocationSettingsRequest(
    TimeFormat TimeFormat,
    DateFormat DateFormat,
    string TimeZone,
    int AvailabilityDays,
    int ClockInWindowMinutes,
    int LateClockInGraceMinutes,
    int BreakLimitMinutes,
    int LunchLimitMinutes,
    bool DevelopmentMode,
    string? SmtpHost,
    int? SmtpPort,
    string? SmtpUsername,
    // Blank/omitted leaves the existing stored password untouched, since
    // GET never sends the real value back down for the field to round-trip.
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
    int LunchLimitMinutes);
