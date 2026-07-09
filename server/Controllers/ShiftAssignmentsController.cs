using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Server.Data;
using Server.Dtos;
using Server.Models;
using Server.Security;

namespace Server.Controllers;

// Admin-only: converts an employee's submitted availability into an actual
// schedule by assigning them to a Shift template on a specific date.
[ApiController]
[Route("api/shift-assignments")]
[Authorize(Policy = "AdminOrAbove")]
public class ShiftAssignmentsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public ActionResult<IEnumerable<ShiftAssignmentDto>> GetForWeek(
        [FromQuery] string? locationCode, [FromQuery] DateOnly weekStartDate)
    {
        var location = ResolveLocation(locationCode);
        if (location is null)
        {
            return BadRequest("A valid locationCode is required.");
        }

        var weekEndDate = weekStartDate.AddDays(6);
        var assignments = db.ShiftAssignments
            .Include(a => a.Shift)
            .Include(a => a.Account)
            .Where(a => a.Shift!.LocationId == location.Id && a.Date >= weekStartDate && a.Date <= weekEndDate)
            .OrderBy(a => a.Date)
            .ThenBy(a => a.Shift!.StartTime)
            .ToList();

        return Ok(assignments.Select(ToDto));
    }

    [HttpPost]
    public ActionResult<ShiftAssignmentDto> Create(CreateShiftAssignmentRequest request)
    {
        var shift = db.Shifts.Include(s => s.Location).SingleOrDefault(s => s.Id == request.ShiftId);
        var account = db.Accounts.Find(request.AccountId);
        if (shift is null || account is null || !CanAccess(shift.Location?.LocationCode) || account.LocationId != shift.LocationId)
        {
            return BadRequest("Shift and employee must belong to the same location you manage.");
        }

        if (account.Role is not (AccountRole.Employee or AccountRole.Lead))
        {
            return BadRequest("Only employees and leads can be scheduled.");
        }

        var alreadyAssigned = db.ShiftAssignments.Any(a =>
            a.ShiftId == shift.Id && a.AccountId == account.Id && a.Date == request.Date);
        if (alreadyAssigned)
        {
            return Conflict("This employee is already assigned to this shift on this date.");
        }

        var assignment = new ShiftAssignment
        {
            ShiftId = shift.Id,
            AccountId = account.Id,
            Date = request.Date,
        };

        db.ShiftAssignments.Add(assignment);
        db.SaveChanges();

        assignment.Shift = shift;
        assignment.Account = account;
        return Ok(ToDto(assignment));
    }

    [HttpPut("{id:int}/move")]
    public ActionResult<ShiftAssignmentDto> Move(int id, MoveShiftAssignmentRequest request)
    {
        var assignment = db.ShiftAssignments
            .Include(a => a.Shift).ThenInclude(s => s!.Location)
            .SingleOrDefault(a => a.Id == id);
        if (assignment is null || !CanAccess(assignment.Shift?.Location?.LocationCode))
        {
            return NotFound();
        }

        var account = db.Accounts.Find(request.AccountId);
        if (account is null || account.LocationId != assignment.Shift!.LocationId)
        {
            return BadRequest("The employee must belong to the same location as the shift.");
        }

        var alreadyAssigned = db.ShiftAssignments.Any(a =>
            a.Id != assignment.Id && a.ShiftId == assignment.ShiftId && a.AccountId == account.Id && a.Date == request.Date);
        if (alreadyAssigned)
        {
            return Conflict("This employee is already assigned to this shift on this date.");
        }

        assignment.AccountId = account.Id;
        assignment.Date = request.Date;
        db.SaveChanges();

        assignment.Account = account;
        return Ok(ToDto(assignment));
    }

    [HttpDelete("{id:int}")]
    public IActionResult Delete(int id)
    {
        var assignment = db.ShiftAssignments
            .Include(a => a.Shift).ThenInclude(s => s!.Location)
            .SingleOrDefault(a => a.Id == id);
        if (assignment is null || !CanAccess(assignment.Shift?.Location?.LocationCode))
        {
            return NotFound();
        }

        db.ShiftAssignments.Remove(assignment);
        db.SaveChanges();
        return NoContent();
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

    private bool CanAccess(string? locationCode) =>
        User.IsInRole(nameof(AccountRole.Sa)) || (locationCode is not null && locationCode == CallerLocationCode());

    private string? CallerLocationCode() =>
        User.FindFirst(TokenService.LocationCodeClaimType)?.Value;

    private static double ComputeHours(TimeOnly start, TimeOnly end)
    {
        var span = end - start;
        if (span < TimeSpan.Zero)
        {
            // Overnight shift (e.g. 22:00-06:00) crosses midnight.
            span += TimeSpan.FromDays(1);
        }

        return Math.Round(span.TotalHours, 2);
    }

    private static ShiftAssignmentDto ToDto(ShiftAssignment a) => new(
        a.Id,
        a.ShiftId,
        a.Shift!.Name,
        a.Shift.StartTime,
        a.Shift.EndTime,
        ComputeHours(a.Shift.StartTime, a.Shift.EndTime),
        a.AccountId,
        a.Account!.FirstName,
        a.Account.LastName,
        a.Date);
}
