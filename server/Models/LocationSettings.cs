namespace Server.Models;

public enum TimeFormat
{
    TwelveHour,
    TwentyFourHour,
}

// One row per Location, created on first access with sensible defaults.
public class LocationSettings
{
    public int Id { get; set; }

    public int LocationId { get; set; }
    public Location? Location { get; set; }

    public TimeFormat TimeFormat { get; set; } = TimeFormat.TwelveHour;

    // IANA time zone id, e.g. "America/Los_Angeles".
    public string TimeZone { get; set; } = "America/Los_Angeles";

    // How many days ahead of today employees are asked to plan
    // availability for (drives reminder copy; the submission-window rule
    // itself is fixed to "next week" — see AvailabilityController).
    public int AvailabilityDays { get; set; } = 7;

    public string? SmtpHost { get; set; }
    public int? SmtpPort { get; set; }
    public string? SmtpUsername { get; set; }
    public string? SmtpPassword { get; set; }
    public bool SmtpUseSsl { get; set; } = true;
    public string? SmtpFromAddress { get; set; }
    public string? SmtpFromName { get; set; }
}
