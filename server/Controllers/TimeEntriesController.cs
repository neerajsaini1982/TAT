using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Server.Data;
using Server.Dtos;
using Server.Models;

namespace Server.Controllers;

// Punch-clock entries. Self-service only for now: an account clocks itself
// in for one of its own published shifts, within a per-location window
// (LocationSettings.ClockInWindowMinutes) before that shift's start time.
// Clocking out isn't implemented yet — every entry is open-ended.
[ApiController]
[Route("api/time-entries")]
[Authorize]
public class TimeEntriesController(AppDbContext db) : ControllerBase
{
    // The caller's clock-in entries for a given date, so the client can show
    // "Clocked In" instead of a button for shifts already punched.
    [HttpGet("mine")]
    public ActionResult<IEnumerable<TimeEntryDto>> GetMine([FromQuery] DateOnly date)
    {
        var accountId = CallerAccountId();
        var entries = db.TimeEntries
            .Where(t => t.AccountId == accountId && t.ShiftAssignment!.Date == date)
            .ToList();

        return Ok(entries.Select(ToDto));
    }

    [HttpPost("clock-in")]
    public ActionResult<TimeEntryDto> ClockIn(ClockInRequest request)
    {
        var accountId = CallerAccountId();
        var assignment = db.ShiftAssignments
            .Include(a => a.Shift)
            .SingleOrDefault(a => a.Id == request.ShiftAssignmentId);

        if (assignment is null || assignment.AccountId != accountId || !assignment.IsPublished)
        {
            return NotFound();
        }

        var today = DateOnly.FromDateTime(DateTime.Now);
        if (assignment.Date != today)
        {
            return BadRequest("You can only clock in for today's shift.");
        }

        if (db.TimeEntries.Any(t => t.ShiftAssignmentId == assignment.Id))
        {
            return Conflict("Already clocked in for this shift.");
        }

        var windowMinutes = db.LocationSettings
            .Where(s => s.LocationId == assignment.Shift!.LocationId)
            .Select(s => (int?)s.ClockInWindowMinutes)
            .SingleOrDefault() ?? 15;

        var scheduledStart = assignment.Date.ToDateTime(assignment.Shift!.StartTime);
        var earliestAllowed = scheduledStart.AddMinutes(-windowMinutes);
        if (DateTime.Now < earliestAllowed)
        {
            return BadRequest($"You can't clock in until {earliestAllowed:h:mm tt}.");
        }

        var entry = new TimeEntry
        {
            AccountId = accountId,
            ShiftAssignmentId = assignment.Id,
            ClockInAt = DateTime.UtcNow,
        };

        db.TimeEntries.Add(entry);
        db.SaveChanges();

        return Ok(ToDto(entry));
    }

    private int CallerAccountId() =>
        int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private static TimeEntryDto ToDto(TimeEntry t) => new(
        t.Id,
        t.ShiftAssignmentId,
        t.AccountId,
        t.ClockInAt,
        t.ClockOutAt);
}
