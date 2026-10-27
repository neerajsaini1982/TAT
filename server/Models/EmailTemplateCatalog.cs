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
        EmailTemplateKeys.PayrollHours => "Payroll Hours",
        EmailTemplateKeys.CoverShiftAssigned => "Cover Shift Assigned",
        EmailTemplateKeys.TimeOffRequested => "Time Off Requested",
        EmailTemplateKeys.TimeOffApproved => "Time Off Approved",
        EmailTemplateKeys.TimeOffDenied => "Time Off Denied",
        EmailTemplateKeys.TimeOffCancelled => "Time Off Cancelled",
        _ => key,
    };

    public static EmailTemplate Default(string key) => key switch
    {
        EmailTemplateKeys.SchedulePublished => new EmailTemplate
        {
            Key = key,
            Subject = "Your schedule for {{weekRange}} is posted",
            BodyHtml = "<p>Hi {{employeeName}},</p><p>Your schedule at {{locationName}} for {{weekRange}} has been posted. Please check your shifts on the app.</p>{{schedule}}",
        },
        EmailTemplateKeys.AvailabilityReminder => new EmailTemplate
        {
            Key = key,
            Subject = "Submit your availability for {{weekRange}}",
            BodyHtml = "<p>Hi {{employeeName}},</p><p>Please submit your availability for {{weekRange}} at {{locationName}} by Saturday.</p>"
                + "<p><a href=\"{{availabilityLink}}\">Submit your availability</a></p>",
        },
        EmailTemplateKeys.LoginCredentials => new EmailTemplate
        {
            Key = key,
            Subject = "Your {{locationName}} login details",
            BodyHtml = "<p>Hi {{employeeName}},</p><p>Here are your login details for {{locationName}}:</p>"
                + "<p>Login link: <a href=\"{{loginLink}}\">{{loginLink}}</a><br/>Your code: <strong>{{userCode}}</strong></p>"
                + "<p>Enter this 6-digit code at the login link above to clock in and view your schedule.</p>",
        },
        EmailTemplateKeys.PayrollHours => new EmailTemplate
        {
            Key = key,
            Subject = "Your hours for {{dateRange}}",
            BodyHtml = "<p>Hi {{employeeName}},</p><p>Here are your recorded hours at {{locationName}} for {{dateRange}}. "
                + "If anything looks wrong, please let your manager know before payroll is submitted.</p>{{hours}}",
        },
        EmailTemplateKeys.CoverShiftAssigned => new EmailTemplate
        {
            Key = key,
            Subject = "You're covering a shift on {{shiftDate}}",
            BodyHtml = "<p>Hi {{employeeName}},</p><p>You've been scheduled to cover {{coveringFor}}'s shift at {{locationName}}:</p>"
                + "<p><strong>{{shiftDate}}</strong><br/>{{shiftName}}, {{shiftTime}}</p>"
                + "<p>Please check your schedule on the app.</p>",
        },
        EmailTemplateKeys.TimeOffRequested => new EmailTemplate
        {
            Key = key,
            Subject = "{{employeeName}} requested time off: {{timeOffDates}}",
            BodyHtml = "<p>{{employeeName}} asked for time off at {{locationName}}:</p>"
                + "<p><strong>{{timeOffType}}</strong>, {{timeOffDates}}<br/>Note: {{note}}</p>"
                + "<p><a href=\"{{adminLink}}\">Review the request</a></p>",
        },
        EmailTemplateKeys.TimeOffApproved => new EmailTemplate
        {
            Key = key,
            Subject = "Your time off on {{timeOffDates}} is approved",
            BodyHtml = "<p>Hi {{employeeName}},</p><p>Your {{timeOffType}} request at {{locationName}} for {{timeOffDates}} has been approved.</p>"
                + "<p>Comment: {{reviewComment}}</p><p><a href=\"{{employeeLink}}\">See your time off</a></p>",
        },
        EmailTemplateKeys.TimeOffDenied => new EmailTemplate
        {
            Key = key,
            Subject = "Your time-off request for {{timeOffDates}} was not approved",
            BodyHtml = "<p>Hi {{employeeName}},</p><p>Your {{timeOffType}} request at {{locationName}} for {{timeOffDates}} was not approved.</p>"
                + "<p>Reason: {{reviewComment}}</p><p><a href=\"{{employeeLink}}\">See your time off</a></p>",
        },
        EmailTemplateKeys.TimeOffCancelled => new EmailTemplate
        {
            Key = key,
            Subject = "{{employeeName}} cancelled time off: {{timeOffDates}}",
            BodyHtml = "<p>{{employeeName}} cancelled their approved {{timeOffType}} at {{locationName}} for {{timeOffDates}}. "
                + "They're available to schedule again on those days.</p><p><a href=\"{{adminLink}}\">See time off</a></p>",
        },
        _ => new EmailTemplate { Key = key, Subject = string.Empty, BodyHtml = string.Empty },
    };

    // Shared token-replace so every sender (AccountsController,
    // ShiftAssignmentsController, ...) renders {{placeholders}} the same way.
    public static string Render(string template, IReadOnlyDictionary<string, string> placeholders)
    {
        foreach (var (token, value) in placeholders)
        {
            template = template.Replace(token, value);
        }

        return template;
    }
}
