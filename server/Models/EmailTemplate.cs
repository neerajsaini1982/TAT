namespace Server.Models;

// A fixed, known set of templates the app can send. New keys can be added
// in code over time; EmailTemplatesController creates a default row for any
// key that doesn't exist yet for a location.
public static class EmailTemplateKeys
{
    public const string SchedulePublished = "SchedulePublished";
    public const string AvailabilityReminder = "AvailabilityReminder";
    public const string LoginCredentials = "LoginCredentials";

    public static readonly IReadOnlyList<string> All = [SchedulePublished, AvailabilityReminder, LoginCredentials];
}

public class EmailTemplate
{
    public int Id { get; set; }

    public int LocationId { get; set; }
    public Location? Location { get; set; }

    public string Key { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string BodyHtml { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
