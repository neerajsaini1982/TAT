using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Server.Data;
using Server.Dtos;
using Server.Models;

namespace Server.Controllers;

// Punch-clock entries. Self-service only: an account clocks itself in for
// one of its own published shifts, within a per-location window
// (LocationSettings.ClockInWindowMinutes) before that shift's start time,
// then moves through at most one Break and one Lunch (taken sequentially,
// one at a time) before clocking out.
[ApiController]
[Route("api/time-entries")]
[Authorize]
public class TimeEntriesController(AppDbContext db) : ControllerBase
{
    // The caller's entries for a given date, so the client can render the
    // right buttons (Clock In / Break / Lunch / Clock Out) for shifts
    // already punched instead of assuming a fresh start.
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

    [HttpPost("{id:int}/break-start")]
    public ActionResult<TimeEntryDto> BreakStart(int id) => Transition(id, entry =>
    {
        if (entry.ClockOutAt is not null)
        {
            return "Already clocked out.";
        }
        if (IsOnLunch(entry))
        {
            return "End lunch before starting a break.";
        }
        if (entry.BreakStartAt is not null)
        {
            return "Break has already been used for this shift.";
        }

        entry.BreakStartAt = DateTime.UtcNow;
        return null;
    });

    [HttpPost("{id:int}/break-end")]
    public ActionResult<TimeEntryDto> BreakEnd(int id) => Transition(id, entry =>
    {
        if (!IsOnBreak(entry))
        {
            return "Not currently on break.";
        }

        entry.BreakEndAt = DateTime.UtcNow;
        return null;
    });

    [HttpPost("{id:int}/lunch-start")]
    public ActionResult<TimeEntryDto> LunchStart(int id) => Transition(id, entry =>
    {
        if (entry.ClockOutAt is not null)
        {
            return "Already clocked out.";
        }
        if (IsOnBreak(entry))
        {
            return "End your break before starting lunch.";
        }
        if (entry.LunchStartAt is not null)
        {
            return "Lunch has already been used for this shift.";
        }

        entry.LunchStartAt = DateTime.UtcNow;
        return null;
    });

    [HttpPost("{id:int}/lunch-end")]
    public ActionResult<TimeEntryDto> LunchEnd(int id) => Transition(id, entry =>
    {
        if (!IsOnLunch(entry))
        {
            return "Not currently at lunch.";
        }

        entry.LunchEndAt = DateTime.UtcNow;
        return null;
    });

    [HttpPost("{id:int}/clock-out")]
    public ActionResult<TimeEntryDto> ClockOut(int id) => Transition(id, entry =>
    {
        if (entry.ClockOutAt is not null)
        {
            return "Already clocked out.";
        }
        if (IsOnBreak(entry))
        {
            return "End your break before clocking out.";
        }
        if (IsOnLunch(entry))
        {
            return "End lunch before clocking out.";
        }

        entry.ClockOutAt = DateTime.UtcNow;
        return null;
    });

    // Applies a state-transition function to the caller's own entry, saving
    // and returning the updated DTO on success, or the returned message as a
    // 400 when the transition isn't valid from the entry's current state.
    private ActionResult<TimeEntryDto> Transition(int id, Func<TimeEntry, string?> apply)
    {
        var entry = db.TimeEntries.SingleOrDefault(t => t.Id == id);
        if (entry is null || entry.AccountId != CallerAccountId())
        {
            return NotFound();
        }

        var error = apply(entry);
        if (error is not null)
        {
            return BadRequest(error);
        }

        db.SaveChanges();
        return Ok(ToDto(entry));
    }

    private static bool IsOnBreak(TimeEntry t) => t.BreakStartAt is not null && t.BreakEndAt is null;

    private static bool IsOnLunch(TimeEntry t) => t.LunchStartAt is not null && t.LunchEndAt is null;

    private int CallerAccountId() =>
        int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private static TimeEntryDto ToDto(TimeEntry t) => new(
        t.Id,
        t.ShiftAssignmentId,
        t.AccountId,
        t.ClockInAt,
        t.BreakStartAt,
        t.BreakEndAt,
        t.LunchStartAt,
        t.LunchEndAt,
        t.ClockOutAt);
}
