namespace Server.Models;

// Display names and default content for every EmailTemplateKeys entry —
// shared by EmailTemplatesController (authoring) and anything that actually
// sends one (e.g. AccountsController.SendCredentials), so the two never
// drift out of sync.
public static class EmailTemplateCatalog
{
    public static string DisplayName(string key) => key switch
    {
        EmailTemplateKeys.SchedulePublished => "Schedule Published",
        EmailTemplateKeys.AvailabilityReminder => "Availability Reminder",
        EmailTemplateKeys.LoginCredentials => "Login Credentials",
        _ => key,
    };

    public static EmailTemplate Default(string key) => key switch
    {
        EmailTemplateKeys.SchedulePublished => new EmailTemplate
        {
            Key = key,
            Subject = "Your schedule for {{weekRange}} is posted",
            BodyHtml = "<p>Hi {{employeeName}},</p><p>Your schedule at {{locationName}} for {{weekRange}} has been posted. Please check your shifts on the app.</p>",
        },
        EmailTemplateKeys.AvailabilityReminder => new EmailTemplate
        {
            Key = key,
            Subject = "Submit your availability for {{weekRange}}",
            BodyHtml = "<p>Hi {{employeeName}},</p><p>Please submit your availability for {{weekRange}} at {{locationName}} by Saturday.</p>",
        },
        EmailTemplateKeys.LoginCredentials => new EmailTemplate
        {
            Key = key,
            Subject = "Your {{locationName}} login details",
            BodyHtml = "<p>Hi {{employeeName}},</p><p>Here are your login details for {{locationName}}:</p>"
                + "<p>Login link: <a href=\"{{loginLink}}\">{{loginLink}}</a><br/>Your code: <strong>{{userCode}}</strong></p>"
                + "<p>Enter this 6-digit code at the login link above to clock in and view your schedule.</p>",
        },
        _ => new EmailTemplate { Key = key, Subject = string.Empty, BodyHtml = string.Empty },
    };
}
