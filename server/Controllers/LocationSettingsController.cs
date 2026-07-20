using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Server.Data;
using Server.Dtos;
using Server.Models;
using Server.Security;

namespace Server.Controllers;

[ApiController]
[Route("api/location-settings")]
public class LocationSettingsController(AppDbContext db) : ControllerBase
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
            settings.LunchLimitMinutes));
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
        settings.SmtpHost = request.SmtpHost;
        settings.SmtpPort = request.SmtpPort;
        settings.SmtpUsername = request.SmtpUsername;
        settings.SmtpUseSsl = request.SmtpUseSsl;
        settings.SmtpFromAddress = request.SmtpFromAddress;
        settings.SmtpFromName = request.SmtpFromName;

        // GET never sends the real password back down, so a blank field
        // here means "unchanged", not "clear it".
        if (!string.IsNullOrEmpty(request.SmtpPassword))
        {
            settings.SmtpPassword = request.SmtpPassword;
        }

        db.SaveChanges();
        return Ok(ToDto(settings));
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
        s.SmtpHost,
        s.SmtpPort,
        s.SmtpUsername,
        s.SmtpUseSsl,
        s.SmtpFromAddress,
        s.SmtpFromName,
        !string.IsNullOrEmpty(s.SmtpPassword));
}
