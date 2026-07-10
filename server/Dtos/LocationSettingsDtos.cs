using Server.Models;

namespace Server.Dtos;

public record LocationSettingsDto(
    TimeFormat TimeFormat,
    DateFormat DateFormat,
    string TimeZone,
    int AvailabilityDays,
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
    string? SmtpHost,
    int? SmtpPort,
    string? SmtpUsername,
    // Blank/omitted leaves the existing stored password untouched, since
    // GET never sends the real value back down for the field to round-trip.
    string? SmtpPassword,
    bool SmtpUseSsl,
    string? SmtpFromAddress,
    string? SmtpFromName);
