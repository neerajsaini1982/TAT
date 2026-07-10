using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Server.Data;
using Server.Dtos;
using Server.Models;
using Server.Security;

namespace Server.Controllers;

[ApiController]
[Route("api/location-settings")]
[Authorize(Policy = "AdminOrAbove")]
public class LocationSettingsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public ActionResult<LocationSettingsDto> Get([FromQuery] string? locationCode)
    {
        var location = ResolveLocation(locationCode);
        if (location is null)
        {
            return BadRequest("A valid locationCode is required.");
        }

        return Ok(ToDto(GetOrCreateSettings(location.Id)));
    }

    [HttpPut]
    public ActionResult<LocationSettingsDto> Update([FromQuery] string? locationCode, UpdateLocationSettingsRequest request)
    {
        var location = ResolveLocation(locationCode);
        if (location is null)
        {
            return BadRequest("A valid locationCode is required.");
        }

        var settings = GetOrCreateSettings(location.Id);
        settings.TimeFormat = request.TimeFormat;
        settings.TimeZone = request.TimeZone;
        settings.AvailabilityDays = request.AvailabilityDays;
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
        s.TimeZone,
        s.AvailabilityDays,
        s.SmtpHost,
        s.SmtpPort,
        s.SmtpUsername,
        s.SmtpUseSsl,
        s.SmtpFromAddress,
        s.SmtpFromName,
        !string.IsNullOrEmpty(s.SmtpPassword));
}
