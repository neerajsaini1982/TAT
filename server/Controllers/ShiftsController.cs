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
        var query = db.Shifts.Include(s => s.Location).AsQueryable();

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
        var shift = db.Shifts.Include(s => s.Location).SingleOrDefault(s => s.Id == id);
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

        var shift = new Shift
        {
            Name = request.Name,
            StartTime = request.StartTime,
            EndTime = request.EndTime,
            IsBreakRequired = request.IsBreakRequired,
            IsLunchRequired = request.IsLunchRequired,
            IsActive = true,
            LocationId = location.Id,
        };

        db.Shifts.Add(shift);
        db.SaveChanges();

        shift.Location = location;
        return CreatedAtAction(nameof(Get), new { id = shift.Id }, ToDto(shift));
    }

    [HttpPut("{id:int}")]
    public ActionResult<ShiftDto> Update(int id, UpdateShiftRequest request)
    {
        var shift = db.Shifts.Include(s => s.Location).SingleOrDefault(s => s.Id == id);
        if (shift is null || !CanAccess(shift))
        {
            return NotFound();
        }

        shift.Name = request.Name;
        shift.StartTime = request.StartTime;
        shift.EndTime = request.EndTime;
        shift.IsBreakRequired = request.IsBreakRequired;
        shift.IsLunchRequired = request.IsLunchRequired;
        shift.IsActive = request.IsActive;
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

    private static ShiftDto ToDto(Shift s) => new(
        s.Id,
        s.Name,
        s.StartTime,
        s.EndTime,
        s.IsBreakRequired,
        s.IsLunchRequired,
        s.IsActive,
        s.Location!.LocationCode);
}
