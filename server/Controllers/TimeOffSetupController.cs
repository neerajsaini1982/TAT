using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Server.Data;
using Server.Dtos;
using Server.Models;
using Server.Security;
using Server.Services;

namespace Server.Controllers;

// A location's time-off setup: its types (paid or unpaid), blackout dates
// and the three time-off settings. Anyone signed in can read the active
// types, blackouts and settings (the request form needs them); only the
// location's admin can change them. Kept apart from LocationSettingsController
// so the big settings form doesn't have to carry these.
[ApiController]
[Route("api")]
[Authorize]
public class TimeOffSetupController(AppDbContext db) : ControllerBase
{
    [HttpGet("time-off-types")]
    public ActionResult<IEnumerable<TimeOffTypeDto>> GetActiveTypes([FromQuery] string? locationCode)
    {
        if (ResolveLocation(locationCode) is not { } location)
        {
            return BadRequest("A valid locationCode is required.");
        }

        TimeOffRules.EnsureDefaultTypes(db, location.Id);
        return Ok(TypeDtos(location.Id).Where(t => t.IsActive));
    }

    [HttpGet("time-off-types/all")]
    [Authorize(Policy = "AdminOrAbove")]
    public ActionResult<IEnumerable<TimeOffTypeDto>> GetAllTypes([FromQuery] string? locationCode)
    {
        if (ResolveLocation(locationCode) is not { } location)
        {
            return BadRequest("A valid locationCode is required.");
        }

        TimeOffRules.EnsureDefaultTypes(db, location.Id);
        return Ok(TypeDtos(location.Id));
    }

    [HttpPost("time-off-types")]
    [Authorize(Policy = "AdminOrAbove")]
    public ActionResult<TimeOffTypeDto> CreateType([FromQuery] string? locationCode, SaveTimeOffTypeRequest request)
    {
        if (ResolveLocation(locationCode) is not { } location)
        {
            return BadRequest("A valid locationCode is required.");
        }

        TimeOffRules.EnsureDefaultTypes(db, location.Id);
        var type = new TimeOffType { LocationId = location.Id };
        if (ApplyType(type, request) is { } error)
        {
            return error;
        }

        db.TimeOffTypes.Add(type);
        db.SaveChanges();
        return Ok(TypeDtos(location.Id).Single(t => t.Id == type.Id));
    }

    [HttpPut("time-off-types/{id:int}")]
    [Authorize(Policy = "AdminOrAbove")]
    public ActionResult<TimeOffTypeDto> UpdateType(int id, SaveTimeOffTypeRequest request)
    {
        var type = db.TimeOffTypes.Find(id);
        if (type is null || !CanManage(type.LocationId))
        {
            return NotFound();
        }

        if (ApplyType(type, request) is { } error)
        {
            return error;
        }

        db.SaveChanges();
        return Ok(TypeDtos(type.LocationId).Single(t => t.Id == type.Id));
    }

    // Only a type nobody has used can be deleted; otherwise deactivate it,
    // so old requests and reports keep their label.
    [HttpDelete("time-off-types/{id:int}")]
    [Authorize(Policy = "AdminOrAbove")]
    public IActionResult DeleteType(int id)
    {
        var type = db.TimeOffTypes.Find(id);
        if (type is null || !CanManage(type.LocationId))
        {
            return NotFound();
        }

        if (db.TimeOffRequests.Any(r => r.TimeOffTypeId == id))
        {
            return Conflict("This type has been used, so it can't be deleted. Turn it off instead.");
        }

        db.TimeOffTypes.Remove(type);
        db.SaveChanges();
        return NoContent();
    }

    [HttpGet("time-off-blackouts")]
    public ActionResult<IEnumerable<TimeOffBlackoutDto>> GetBlackouts([FromQuery] string? locationCode)
    {
        if (ResolveLocation(locationCode) is not { } location)
        {
            return BadRequest("A valid locationCode is required.");
        }

        return Ok(db.TimeOffBlackouts
            .Where(b => b.LocationId == location.Id)
            .OrderBy(b => b.StartDate)
            .Select(b => new TimeOffBlackoutDto(b.Id, b.Name, b.StartDate, b.EndDate))
            .ToList());
    }

    [HttpPost("time-off-blackouts")]
    [Authorize(Policy = "AdminOrAbove")]
    public ActionResult<TimeOffBlackoutDto> CreateBlackout([FromQuery] string? locationCode, SaveTimeOffBlackoutRequest request)
    {
        if (ResolveLocation(locationCode) is not { } location)
        {
            return BadRequest("A valid locationCode is required.");
        }

        var blackout = new TimeOffBlackout { LocationId = location.Id };
        if (ApplyBlackout(blackout, request) is { } error)
        {
            return BadRequest(error);
        }

        db.TimeOffBlackouts.Add(blackout);
        db.SaveChanges();
        return Ok(new TimeOffBlackoutDto(blackout.Id, blackout.Name, blackout.StartDate, blackout.EndDate));
    }

    [HttpPut("time-off-blackouts/{id:int}")]
    [Authorize(Policy = "AdminOrAbove")]
    public ActionResult<TimeOffBlackoutDto> UpdateBlackout(int id, SaveTimeOffBlackoutRequest request)
    {
        var blackout = db.TimeOffBlackouts.Find(id);
        if (blackout is null || !CanManage(blackout.LocationId))
        {
            return NotFound();
        }

        if (ApplyBlackout(blackout, request) is { } error)
        {
            return BadRequest(error);
        }

        db.SaveChanges();
        return Ok(new TimeOffBlackoutDto(blackout.Id, blackout.Name, blackout.StartDate, blackout.EndDate));
    }

    // Removing a blackout doesn't touch requests made around it.
    [HttpDelete("time-off-blackouts/{id:int}")]
    [Authorize(Policy = "AdminOrAbove")]
    public IActionResult DeleteBlackout(int id)
    {
        var blackout = db.TimeOffBlackouts.Find(id);
        if (blackout is null || !CanManage(blackout.LocationId))
        {
            return NotFound();
        }

        db.TimeOffBlackouts.Remove(blackout);
        db.SaveChanges();
        return NoContent();
    }

    [HttpGet("time-off-settings")]
    public ActionResult<TimeOffSettingsDto> GetSettings([FromQuery] string? locationCode)
    {
        if (ResolveLocation(locationCode) is not { } location)
        {
            return BadRequest("A valid locationCode is required.");
        }

        var s = TimeOffRules.SettingsFor(db, location.Id);
        return Ok(new TimeOffSettingsDto(s.TimeOffEnabled, s.TimeOffMinNoticeDays, s.TimeOffFullDayMinutes));
    }

    [HttpPut("time-off-settings")]
    [Authorize(Policy = "AdminOrAbove")]
    public ActionResult<TimeOffSettingsDto> UpdateSettings([FromQuery] string? locationCode, TimeOffSettingsDto request)
    {
        if (ResolveLocation(locationCode) is not { } location)
        {
            return BadRequest("A valid locationCode is required.");
        }

        if (request.TimeOffMinNoticeDays is < 0 or > 365)
        {
            return BadRequest("Minimum notice must be between 0 and 365 days.");
        }

        if (request.TimeOffFullDayMinutes is <= 0 or > 24 * 60)
        {
            return BadRequest("A full day must be between 1 minute and 24 hours.");
        }

        var settings = db.LocationSettings.SingleOrDefault(s => s.LocationId == location.Id);
        if (settings is null)
        {
            settings = new LocationSettings { LocationId = location.Id };
            db.LocationSettings.Add(settings);
        }

        settings.TimeOffEnabled = request.TimeOffEnabled;
        settings.TimeOffMinNoticeDays = request.TimeOffMinNoticeDays is 0 ? null : request.TimeOffMinNoticeDays;
        settings.TimeOffFullDayMinutes = request.TimeOffFullDayMinutes;
        db.SaveChanges();
        return Ok(new TimeOffSettingsDto(settings.TimeOffEnabled, settings.TimeOffMinNoticeDays, settings.TimeOffFullDayMinutes));
    }

    private ActionResult? ApplyType(TimeOffType type, SaveTimeOffTypeRequest request)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length == 0 || name.Length > 50)
        {
            return BadRequest("Give the type a name (up to 50 characters).");
        }

        if (name.Equals("Sick", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest("Sick time is built in and always paid, so it can't be added as a type.");
        }

        if (db.TimeOffTypes.Any(t => t.LocationId == type.LocationId && t.Id != type.Id && t.Name.ToLower() == name.ToLower()))
        {
            return Conflict("There's already a type with that name.");
        }

        type.Name = name;
        type.IsPaid = request.IsPaid;
        type.IsActive = request.IsActive;
        type.SortOrder = request.SortOrder;
        type.Color = string.IsNullOrWhiteSpace(request.Color) ? null : request.Color.Trim();
        type.PayrollCode = string.IsNullOrWhiteSpace(request.PayrollCode) ? null : request.PayrollCode.Trim();
        return null;
    }

    private static string? ApplyBlackout(TimeOffBlackout blackout, SaveTimeOffBlackoutRequest request)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length == 0 || name.Length > 80)
        {
            return "Give the blackout a name (up to 80 characters).";
        }

        if (request.EndDate < request.StartDate)
        {
            return "The end date can't be before the start date.";
        }

        blackout.Name = name;
        blackout.StartDate = request.StartDate;
        blackout.EndDate = request.EndDate;
        return null;
    }

    private List<TimeOffTypeDto> TypeDtos(int locationId)
    {
        var usedIds = db.TimeOffRequests
            .Where(r => r.LocationId == locationId)
            .Select(r => r.TimeOffTypeId)
            .Distinct()
            .ToHashSet();
        return db.TimeOffTypes
            .Where(t => t.LocationId == locationId)
            .OrderBy(t => t.SortOrder).ThenBy(t => t.Name)
            .ToList()
            .Select(t => new TimeOffTypeDto(t.Id, t.Name, t.IsPaid, t.IsActive, t.SortOrder, t.Color, t.PayrollCode, usedIds.Contains(t.Id)))
            .ToList();
    }

    private bool CanManage(int locationId) =>
        User.IsInRole(nameof(AccountRole.Sa))
        || db.Locations.Any(l => l.Id == locationId && l.LocationCode == User.FindFirst(TokenService.LocationCodeClaimType)!.Value);

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
}
