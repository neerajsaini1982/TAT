using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Server.Data;
using Server.Dtos;
using Server.Models;
using Server.Security;

namespace Server.Controllers;

// Converts an employee's submitted availability into an actual schedule by
// assigning them to a Shift template on a specific date. Managing
// assignments is Admin/Sa-only (see the per-action policy below); any
// authenticated account can read back its own via GetMine.
[ApiController]
[Route("api/shift-assignments")]
[Authorize]
public class ShiftAssignmentsController(AppDbContext db) : ControllerBase
{
    // Self-service: whoever is logged in sees only their own upcoming
    // schedule, so an Employee/Lead/Admin can answer "when am I working
    // next, and for how long" without needing the admin roster view.
    [HttpGet("mine")]
    public ActionResult<IEnumerable<ShiftAssignmentDto>> GetMine()
    {
        var accountId = CallerAccountId();
        var today = DateOnly.FromDateTime(DateTime.Now);

        var assignments = db.ShiftAssignments
            .Include(a => a.Shift)
            .Include(a => a.Account)
            .Where(a => a.AccountId == accountId && a.Date >= today && a.IsPublished)
            .OrderBy(a => a.Date)
            .ThenBy(a => a.Shift!.StartTime)
            .ToList();

        return Ok(assignments.Select(ToDto));
    }

    // Shown on the employee login screen, before anyone has signed in — so
    // it's deliberately public and returns only what's safe to show on a
    // shared/kiosk screen (first name + last initial, no account ids).
    [HttpGet("today")]
    [AllowAnonymous]
    public ActionResult<IEnumerable<TodayScheduleEntryDto>> GetToday([FromQuery] string locationCode)
    {
        var location = db.Locations.SingleOrDefault(l => l.LocationCode == locationCode);
        if (location is null)
        {
            return BadRequest("A valid locationCode is required.");
        }

        var today = DateOnly.FromDateTime(DateTime.Now);
        var assignments = db.ShiftAssignments
            .Include(a => a.Shift)
            .Include(a => a.Account)
            .Where(a => a.Shift!.LocationId == location.Id && a.Date == today && a.IsPublished)
            .OrderBy(a => a.Shift!.StartTime)
            .ToList();

        return Ok(assignments.Select(a => new TodayScheduleEntryDto(
            a.Shift!.Name,
            a.Shift.StartTime,
            a.Shift.EndTime,
            $"{a.Account!.FirstName} {a.Account.LastName[..1]}.")));
    }

    // Bulk-marks every assignment in a location/week as published so it
    // starts showing up in GetMine for the employees on it. Until this is
    // called, the admin schedule grid is a draft/preview only.
    [HttpPost("publish")]
    [Authorize(Policy = "AdminOrAbove")]
    public IActionResult Publish(PublishScheduleRequest request)
    {
        var location = ResolveLocation(request.LocationCode);
        if (location is null)
        {
            return BadRequest("A valid locationCode is required.");
        }

        var weekEndDate = request.WeekStartDate.AddDays(6);
        var assignments = db.ShiftAssignments
            .Include(a => a.Shift)
            .Where(a => a.Shift!.LocationId == location.Id && a.Date >= request.WeekStartDate && a.Date <= weekEndDate)
            .ToList();

        var publishedAt = DateTime.UtcNow;
        foreach (var assignment in assignments)
        {
            assignment.IsPublished = true;
            assignment.PublishedAt = publishedAt;
        }

        db.SaveChanges();
        return NoContent();
    }

    [HttpGet]
    [Authorize(Policy = "AdminOrAbove")]
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
    [Authorize(Policy = "AdminOrAbove")]
    public ActionResult<ShiftAssignmentDto> Create(CreateShiftAssignmentRequest request)
    {
        var shift = db.Shifts.Include(s => s.Location).SingleOrDefault(s => s.Id == request.ShiftId);
        var account = db.Accounts.Find(request.AccountId);
        if (shift is null || account is null || !CanAccess(shift.Location?.LocationCode) || account.LocationId != shift.LocationId)
        {
            return BadRequest("Shift and employee must belong to the same location you manage.");
        }

        if (account.Role is not (AccountRole.Employee or AccountRole.Lead or AccountRole.Admin))
        {
            return BadRequest("Only employees, leads, and admins can be scheduled.");
        }

        if (!IsAvailable(account.Id, request.Date))
        {
            return BadRequest("This employee is not available on this date.");
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
    [Authorize(Policy = "AdminOrAbove")]
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

        if (account.Role is not (AccountRole.Employee or AccountRole.Lead or AccountRole.Admin))
        {
            return BadRequest("Only employees, leads, and admins can be scheduled.");
        }

        if (!IsAvailable(account.Id, request.Date))
        {
            return BadRequest("This employee is not available on this date.");
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
    [Authorize(Policy = "AdminOrAbove")]
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

    // An employee who hasn't said they're available that day (including
    // never having submitted anything for that week) can't be assigned a
    // shift there.
    private bool IsAvailable(int accountId, DateOnly date) =>
        db.Availabilities
            .Where(a => a.AccountId == accountId)
            .SelectMany(a => a.Days)
            .Any(d => d.Date == date && d.IsAvailable);

    private bool CanAccess(string? locationCode) =>
        User.IsInRole(nameof(AccountRole.Sa)) || (locationCode is not null && locationCode == CallerLocationCode());

    private string? CallerLocationCode() =>
        User.FindFirst(TokenService.LocationCodeClaimType)?.Value;

    private int CallerAccountId() =>
        int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

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
        a.Date,
        a.IsPublished);
}
