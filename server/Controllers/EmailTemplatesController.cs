using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Server.Data;
using Server.Dtos;
using Server.Models;
using Server.Security;
using Server.Services;

namespace Server.Controllers;

// Manages the fixed set of email templates (see EmailTemplateKeys) used
// elsewhere in the app. The real sends live with their features
// (AccountsController.SendCredentials, ShiftAssignmentsController.Publish,
// ReportsController.EmailHoursReport); the only sending here is SendTest,
// which mails the admin a [TEST] copy of a saved template.
[ApiController]
[Route("api/email-templates")]
[Authorize(Policy = "AdminOrAbove")]
public class EmailTemplatesController(AppDbContext db, IEmailSender emailSender) : ControllerBase
{
    [HttpGet]
    public ActionResult<IEnumerable<EmailTemplateDto>> GetAll([FromQuery] string? locationCode)
    {
        var location = ResolveLocation(locationCode);
        if (location is null)
        {
            return BadRequest("A valid locationCode is required.");
        }

        var existing = db.EmailTemplates
            .Where(t => t.LocationId == location.Id)
            .ToDictionary(t => t.Key);

        var result = EmailTemplateKeys.All.Select(key =>
            ToDto(existing.TryGetValue(key, out var t) ? t : EmailTemplateCatalog.Default(key)));

        return Ok(result);
    }

    [HttpPut("{key}")]
    public ActionResult<EmailTemplateDto> Update(string key, UpdateEmailTemplateRequest request, [FromQuery] string? locationCode)
    {
        if (!EmailTemplateKeys.All.Contains(key))
        {
            return NotFound();
        }

        var location = ResolveLocation(locationCode);
        if (location is null)
        {
            return BadRequest("A valid locationCode is required.");
        }

        var template = db.EmailTemplates.SingleOrDefault(t => t.LocationId == location.Id && t.Key == key);
        if (template is null)
        {
            template = new EmailTemplate { LocationId = location.Id, Key = key };
            db.EmailTemplates.Add(template);
        }

        template.Subject = request.Subject;
        template.BodyHtml = request.BodyHtml;
        template.UpdatedAt = DateTime.UtcNow;
        db.SaveChanges();

        return Ok(ToDto(template));
    }

    // Sends the saved template (or its default) to ToAddress with sample
    // placeholder values, so an admin can see it in a real inbox before it
    // goes to employees. PayrollHours uses real data instead — the first
    // employee with hours over the trailing week, same default range as the
    // Payroll Report page — and only falls back to made-up hours when nobody
    // has any.
    [HttpPost("{key}/test")]
    public async Task<IActionResult> SendTest(string key, SendTestEmailTemplateRequest request, [FromQuery] string? locationCode)
    {
        if (!EmailTemplateKeys.All.Contains(key))
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(request.ToAddress))
        {
            return BadRequest("Enter an address to send the test to.");
        }

        var location = ResolveLocation(locationCode);
        if (location is null)
        {
            return BadRequest("A valid locationCode is required.");
        }

        var settings = db.LocationSettings.SingleOrDefault(s => s.LocationId == location.Id);
        if (settings is null || string.IsNullOrWhiteSpace(settings.SmtpHost))
        {
            return BadRequest("SMTP is not configured for this location. Set it up under Settings first.");
        }

        var template = db.EmailTemplates.SingleOrDefault(t => t.LocationId == location.Id && t.Key == key)
            ?? EmailTemplateCatalog.Default(key);
        var placeholders = key == EmailTemplateKeys.PayrollHours
            ? PayrollTestPlaceholders(location, settings)
            : SampleTestPlaceholders(location, settings);

        try
        {
            await emailSender.SendAsync(
                settings,
                request.ToAddress,
                $"[TEST] {EmailTemplateCatalog.Render(template.Subject, placeholders)}",
                EmailTemplateCatalog.Render(template.BodyHtml, placeholders));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status502BadGateway, "Failed to send email. Check the SMTP settings and try again.");
        }

        return NoContent();
    }

    private Dictionary<string, string> PayrollTestPlaceholders(Location location, LocationSettings settings)
    {
        var endDate = LocationToday(settings);
        var startDate = endDate.AddDays(-6);
        var row = ReportsController.BuildHoursReport(db, location, startDate, endDate, onlyAccountId: null)
            .FirstOrDefault(r => r.TotalNetWorkedMinutes + r.TotalSickMinutes > 0)
            ?? SampleHoursRow(startDate);
        return PayrollHoursEmail.Placeholders(row, location.Name, startDate, endDate, settings.DateFormat);
    }

    private Dictionary<string, string> SampleTestPlaceholders(Location location, LocationSettings settings)
    {
        var weekStart = LocationToday(settings);
        var sampleShift = new Shift { Name = "Sample Shift", StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(17, 0) };
        var sampleAssignments = Enumerable.Range(0, 3)
            .Select(i => new ShiftAssignment { Date = weekStart.AddDays(i), Shift = sampleShift });

        return new Dictionary<string, string>
        {
            ["{{employeeName}}"] = "Sample Employee",
            ["{{locationName}}"] = location.Name,
            ["{{weekRange}}"] = $"{weekStart.ToString("MMM d")} – {weekStart.AddDays(6).ToString("MMM d")}",
            ["{{schedule}}"] = ShiftAssignmentsController.BuildScheduleHtml(sampleAssignments, settings.TimeFormat),
            ["{{userCode}}"] = "123456",
            ["{{loginLink}}"] = $"{Request.Scheme}://{Request.Host}/",
        };
    }

    // Two made-up 8.5h days, one with an hour of overtime — only used when
    // nobody at the location has real hours in the trailing week.
    private static EmployeeHoursReportDto SampleHoursRow(DateOnly startDate)
    {
        DailyHoursDto Day(DateOnly date, int regular, int overtime) => new(
            date, regular + overtime + 30, 0, 30, regular + overtime, regular, regular, overtime, 0,
            false, null, false, null, false, false, false, [], 0, null, []);

        var days = new List<DailyHoursDto> { Day(startDate, 480, 30), Day(startDate.AddDays(1), 480, 30) };
        return new EmployeeHoursReportDto(
            0, "Sample Employee", false, 1080, 0, 60, 1020, 960, 960, 60, 0, 0, 0, 0, days);
    }

    private static DateOnly LocationToday(LocationSettings settings)
    {
        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(settings.TimeZone);
            return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone));
        }
        catch (TimeZoneNotFoundException)
        {
            return DateOnly.FromDateTime(DateTime.UtcNow);
        }
    }

    private Location? ResolveLocation(string? locationCode)
    {
        if (User.IsInRole(nameof(AccountRole.Sa)))
        {
            return string.IsNullOrWhiteSpace(locationCode)
                ? null
                : db.Locations.SingleOrDefault(l => l.LocationCode == locationCode);
        }

        var callerLocationCode = User.FindFirst(TokenService.LocationCodeClaimType)?.Value;
        return db.Locations.SingleOrDefault(l => l.LocationCode == callerLocationCode);
    }

    private static EmailTemplateDto ToDto(EmailTemplate t) =>
        new(t.Key, EmailTemplateCatalog.DisplayName(t.Key), t.Subject, t.BodyHtml, t.UpdatedAt);
}
