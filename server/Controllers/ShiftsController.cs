using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Server.Data;
using Server.Dtos;
using Server.Models;
using Server.Security;

namespace Server.Controllers;

[ApiController]
[Route("api/shifts")]
[Authorize(Policy = "AdminOrAbove")]
public class ShiftsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public ActionResult<IEnumerable<ShiftDto>> GetAll([FromQuery] string? locationCode)
    {
        var query = db.Shifts.Include(s => s.Location).Include(s => s.ScheduledBreaks).AsQueryable();

        if (User.IsInRole(nameof(AccountRole.Sa)))
        {
            if (!string.IsNullOrWhiteSpace(locationCode))
            {
                query = query.Where(s => s.Location!.LocationCode == locationCode);
            }
        }
        else
        {
            var callerLocationCode = CallerLocationCode();
            query = query.Where(s => s.Location!.LocationCode == callerLocationCode);
        }

        return Ok(query.OrderBy(s => s.StartTime).Select(ToDto));
    }

    [HttpGet("{id:int}")]
    public ActionResult<ShiftDto> Get(int id)
    {
        var shift = db.Shifts.Include(s => s.Location).Include(s => s.ScheduledBreaks).SingleOrDefault(s => s.Id == id);
        if (shift is null || !CanAccess(shift))
        {
            return NotFound();
        }

        return Ok(ToDto(shift));
    }

    [HttpPost]
    public ActionResult<ShiftDto> Create(CreateShiftRequest request)
    {
        var locationId = User.IsInRole(nameof(AccountRole.Sa))
            ? request.LocationId
            : db.Locations.Single(l => l.LocationCode == CallerLocationCode()).Id;

        var location = locationId.HasValue ? db.Locations.Find(locationId.Value) : null;
        if (location is null)
        {
            return BadRequest("A valid locationId is required.");
        }

        var validationError = ValidateScheduledBreaks(request.ScheduledBreaks, request.StartTime, request.EndTime);
        if (validationError is not null)
        {
            return BadRequest(validationError);
        }

        var shift = new Shift
        {
            Name = request.Name,
            StartTime = request.StartTime,
            EndTime = request.EndTime,
            IsActive = true,
            LocationId = location.Id,
            ScheduledBreaks = request.ScheduledBreaks.Select(ToEntity).ToList(),
        };

        db.Shifts.Add(shift);
        db.SaveChanges();

        shift.Location = location;
        return CreatedAtAction(nameof(Get), new { id = shift.Id }, ToDto(shift));
    }

    [HttpPut("{id:int}")]
    public ActionResult<ShiftDto> Update(int id, UpdateShiftRequest request)
    {
        var shift = db.Shifts
            .Include(s => s.Location)
            .Include(s => s.ScheduledBreaks)
            .SingleOrDefault(s => s.Id == id);
        if (shift is null || !CanAccess(shift))
        {
            return NotFound();
        }

        var validationError = ValidateScheduledBreaks(request.ScheduledBreaks, request.StartTime, request.EndTime);
        if (validationError is not null)
        {
            return BadRequest(validationError);
        }

        shift.Name = request.Name;
        shift.StartTime = request.StartTime;
        shift.EndTime = request.EndTime;
        shift.IsActive = request.IsActive;

        // Wholesale replace rather than diff — simplest correct approach for
        // a list the admin edits freely (add/remove/retime any window).
        db.ScheduledBreaks.RemoveRange(shift.ScheduledBreaks);
        shift.ScheduledBreaks = request.ScheduledBreaks.Select(ToEntity).ToList();

        db.SaveChanges();

        return Ok(ToDto(shift));
    }

    [HttpDelete("{id:int}")]
    public IActionResult Delete(int id)
    {
        var shift = db.Shifts.Include(s => s.Location).SingleOrDefault(s => s.Id == id);
        if (shift is null || !CanAccess(shift))
        {
            return NotFound();
        }

        db.Shifts.Remove(shift);
        db.SaveChanges();
        return NoContent();
    }

    private bool CanAccess(Shift shift) =>
        User.IsInRole(nameof(AccountRole.Sa)) || shift.Location?.LocationCode == CallerLocationCode();

    private string? CallerLocationCode() =>
        User.FindFirst(TokenService.LocationCodeClaimType)?.Value;

    // Every window must fall within the shift's own span and have a
    // sensible order; windows can't overlap each other regardless of Kind
    // (an employee can't be on a break and a lunch at the same time).
    private static string? ValidateScheduledBreaks(List<ScheduledBreakDto> breaks, TimeOnly shiftStart, TimeOnly shiftEnd)
    {
        foreach (var b in breaks)
        {
            if (b.EndTime <= b.StartTime)
            {
                return $"{b.Kind} end time must be after its start time.";
            }
            if (b.StartTime < shiftStart || b.EndTime > shiftEnd)
            {
                return $"{b.Kind} ({b.StartTime:h\\:mm}–{b.EndTime:h\\:mm}) must fall within the shift's start and end time.";
            }
        }

        foreach (var a in breaks.OrderBy(b => b.StartTime))
        {
            foreach (var b in breaks.Where(x => x != a))
            {
                if (a.StartTime < b.EndTime && b.StartTime < a.EndTime)
                {
                    return "Scheduled breaks and lunches can't overlap.";
                }
            }
        }

        return null;
    }

    private static ScheduledBreak ToEntity(ScheduledBreakDto dto) => new()
    {
        Kind = dto.Kind,
        StartTime = dto.StartTime,
        EndTime = dto.EndTime,
    };

    private static ShiftDto ToDto(Shift s) => new(
        s.Id,
        s.Name,
        s.StartTime,
        s.EndTime,
        s.ScheduledBreaks
            .OrderBy(b => b.StartTime)
            .Select(b => new ScheduledBreakDto(b.Kind, b.StartTime, b.EndTime))
            .ToList(),
        s.IsActive,
        s.Location!.LocationCode);
}
