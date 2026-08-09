using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Server.Data;
using Server.Dtos;
using Server.Models;
using Server.Security;
using Server.Services;

namespace Server.Controllers;

[ApiController]
[Route("api/location-settings")]
public class LocationSettingsController(AppDbContext db, IEmailSender emailSender) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = "AdminOrAbove")]
    public ActionResult<LocationSettingsDto> Get([FromQuery] string? locationCode)
    {
        var location = ResolveLocation(locationCode);
        if (location is null)
        {
            return BadRequest("A valid locationCode is required.");
        }

        return Ok(ToDto(GetOrCreateSettings(location.Id)));
    }

    // Self-service: lets any signed-in account (e.g. an Employee) read just
    // the Clock In window, without exposing SMTP credentials etc.
    [HttpGet("mine")]
    [Authorize]
    public ActionResult<EmployeeLocationSettingsDto> GetMine()
    {
        var location = db.Locations.SingleOrDefault(l => l.LocationCode == CallerLocationCode());
        if (location is null)
        {
            return NotFound();
        }

        var settings = GetOrCreateSettings(location.Id);
        return Ok(new EmployeeLocationSettingsDto(
            settings.TimeFormat,
            settings.TimeZone,
            settings.ClockInWindowMinutes,
            settings.LateClockInGraceMinutes,
            settings.BreakLimitMinutes,
            settings.LunchLimitMinutes,
            settings.GetNextPayDate(DateOnly.FromDateTime(DateTime.Now))));
    }

    [HttpPut]
    [Authorize(Policy = "AdminOrAbove")]
    public ActionResult<LocationSettingsDto> Update([FromQuery] string? locationCode, UpdateLocationSettingsRequest request)
    {
        var location = ResolveLocation(locationCode);
        if (location is null)
        {
            return BadRequest("A valid locationCode is required.");
        }

        var settings = GetOrCreateSettings(location.Id);
        settings.TimeFormat = request.TimeFormat;
        settings.DateFormat = request.DateFormat;
        settings.TimeZone = request.TimeZone;
        settings.AvailabilityDays = request.AvailabilityDays;
        settings.ClockInWindowMinutes = request.ClockInWindowMinutes;
        settings.LateClockInGraceMinutes = request.LateClockInGraceMinutes;
        settings.BreakLimitMinutes = request.BreakLimitMinutes;
        settings.LunchLimitMinutes = request.LunchLimitMinutes;
        settings.DevelopmentMode = request.DevelopmentMode;
        settings.ScheduleVisibilityEnabled = request.ScheduleVisibilityEnabled;
        settings.AdminSeesAllSchedules = request.AdminSeesAllSchedules;
        settings.LeadSeesAllSchedules = request.LeadSeesAllSchedules;
        settings.EmployeeSeesAllSchedules = request.EmployeeSeesAllSchedules;
        settings.ClockInAnywhere = request.ClockInAnywhere;
        settings.SmtpHost = request.SmtpHost;
        settings.SmtpPort = request.SmtpPort;
        settings.SmtpUsername = request.SmtpUsername;
        settings.SmtpUseSsl = request.SmtpUseSsl;
        settings.SmtpFromAddress = request.SmtpFromAddress;
        settings.SmtpFromName = request.SmtpFromName;
        settings.PayDayStartDate = request.PayDayStartDate;
        settings.PayPeriodDays = request.PayPeriodDays;

        // GET never sends the real password back down, so a blank field
        // here means "unchanged", not "clear it".
        if (!string.IsNullOrEmpty(request.SmtpPassword))
        {
            settings.SmtpPassword = request.SmtpPassword;
        }

        db.SaveChanges();
        return Ok(ToDto(settings));
    }

    // Sends a one-off email through whatever SMTP fields are currently in
    // the form, so an admin can confirm credentials work before committing
    // to Save (and can re-test after, without retyping the password).
    [HttpPost("test-email")]
    [Authorize(Policy = "AdminOrAbove")]
    public async Task<IActionResult> SendTestEmail([FromQuery] string? locationCode, SendTestEmailRequest request)
    {
        var location = ResolveLocation(locationCode);
        if (location is null)
        {
            return BadRequest("A valid locationCode is required.");
        }

        if (string.IsNullOrWhiteSpace(request.ToAddress))
        {
            return BadRequest("Enter an email address to send the test to.");
        }

        var saved = db.LocationSettings.SingleOrDefault(s => s.LocationId == location.Id);
        var testSettings = new LocationSettings
        {
            LocationId = location.Id,
            SmtpHost = request.SmtpHost,
            SmtpPort = request.SmtpPort,
            SmtpUsername = request.SmtpUsername,
            SmtpPassword = string.IsNullOrEmpty(request.SmtpPassword) ? saved?.SmtpPassword : request.SmtpPassword,
            SmtpUseSsl = request.SmtpUseSsl,
            SmtpFromAddress = request.SmtpFromAddress,
            SmtpFromName = request.SmtpFromName,
        };

        try
        {
            await emailSender.SendAsync(
                testSettings,
                request.ToAddress,
                "TAT test email",
                $"<p>This is a test email from {location.Name} to confirm the SMTP settings are working.</p>");
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            // This endpoint exists purely to surface SMTP problems to an
            // Admin/Sa, so the real provider error (e.g. Gmail auth
            // rejection) is more useful here than a generic message.
            return StatusCode(StatusCodes.Status502BadGateway, $"Failed to send test email: {ex.Message}");
        }

        return NoContent();
    }

    private LocationSettings GetOrCreateSettings(int locationId)
    {
        var settings = db.LocationSettings.SingleOrDefault(s => s.LocationId == locationId);
        if (settings is null)
        {
            settings = new LocationSettings { LocationId = locationId };
            db.LocationSettings.Add(settings);
            db.SaveChanges();
        }

        return settings;
    }

    private Location? ResolveLocation(string? locationCode)
    {
        if (User.IsInRole(nameof(AccountRole.Sa)))
        {
            return string.IsNullOrWhiteSpace(locationCode)
                ? null
                : db.Locations.SingleOrDefault(l => l.LocationCode == locationCode);
        }

        var callerLocationCode = CallerLocationCode();
        return db.Locations.SingleOrDefault(l => l.LocationCode == callerLocationCode);
    }

    private string? CallerLocationCode() =>
        User.FindFirst(TokenService.LocationCodeClaimType)?.Value;

    private static LocationSettingsDto ToDto(LocationSettings s) => new(
        s.TimeFormat,
        s.DateFormat,
        s.TimeZone,
        s.AvailabilityDays,
        s.ClockInWindowMinutes,
        s.LateClockInGraceMinutes,
        s.BreakLimitMinutes,
        s.LunchLimitMinutes,
        s.DevelopmentMode,
        s.ScheduleVisibilityEnabled,
        s.AdminSeesAllSchedules,
        s.LeadSeesAllSchedules,
        s.EmployeeSeesAllSchedules,
        s.ClockInAnywhere,
        s.SmtpHost,
        s.SmtpPort,
        s.SmtpUsername,
        s.SmtpUseSsl,
        s.SmtpFromAddress,
        s.SmtpFromName,
        !string.IsNullOrEmpty(s.SmtpPassword),
        s.PayDayStartDate,
        s.PayPeriodDays,
        s.GetNextPayDate(DateOnly.FromDateTime(DateTime.Now)));
}
