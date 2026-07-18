using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Server.Data;
using Server.Dtos;
using Server.Hubs;
using Server.Models;
using Server.Security;

namespace Server.Controllers;

// Punch-clock entries. Self-service only for the employee's own punches: an
// account clocks itself in for one of its own published shifts, within a
// per-location window (LocationSettings.ClockInWindowMinutes) before that
// shift's start time, then moves through at most one Break, one Lunch, and
// (for longer shifts) a second Break after Lunch has ended — taken
// sequentially, one at a time — before clocking out. Leads/Admins get one
// override on top of that: AdminClockOut, for closing out an entry the
// employee didn't (see its doc comment).
[ApiController]
[Route("api/time-entries")]
[Authorize]
public class TimeEntriesController(AppDbContext db, IScheduleNotifier notifier) : ControllerBase
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

    // Every entry for a location/date, so the admin schedule grid can see
    // who's clocked in, on break, etc. — GetMine only ever shows the
    // caller's own entries.
    [HttpGet]
    [Authorize(Policy = "LeadOrAbove")]
    public ActionResult<IEnumerable<TimeEntryDto>> GetForLocation([FromQuery] string locationCode, [FromQuery] DateOnly date)
    {
        if (!CanAccess(locationCode))
        {
            return BadRequest("A valid locationCode is required.");
        }

        var location = db.Locations.SingleOrDefault(l => l.LocationCode == locationCode);
        if (location is null)
        {
            return BadRequest("A valid locationCode is required.");
        }

        var entries = db.TimeEntries
            .Where(t => t.ShiftAssignment!.Date == date && t.ShiftAssignment.Shift!.LocationId == location.Id)
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

        // Showing up supersedes an earlier absence mark.
        assignment.IsAbsent = false;
        assignment.AbsenceNote = null;

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
        if (IsOnLunch(entry) || IsOnBreak2(entry))
        {
            return "End your current break or lunch before starting a break.";
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
        if (IsOnBreak(entry) || IsOnBreak2(entry))
        {
            return "End your current break before starting lunch.";
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

    // A second break, only meaningful for longer shifts — available any
    // time after lunch has ended (see IsOnLunch/LunchEndAt below).
    [HttpPost("{id:int}/break2-start")]
    public ActionResult<TimeEntryDto> Break2Start(int id) => Transition(id, entry =>
    {
        if (entry.ClockOutAt is not null)
        {
            return "Already clocked out.";
        }
        if (entry.LunchEndAt is null)
        {
            return "Finish your lunch before starting a second break.";
        }
        if (IsOnBreak(entry) || IsOnLunch(entry))
        {
            return "End your current break or lunch before starting a second break.";
        }
        if (entry.Break2StartAt is not null)
        {
            return "Second break has already been used for this shift.";
        }

        entry.Break2StartAt = DateTime.UtcNow;
        return null;
    });

    [HttpPost("{id:int}/break2-end")]
    public ActionResult<TimeEntryDto> Break2End(int id) => Transition(id, entry =>
    {
        if (!IsOnBreak2(entry))
        {
            return "Not currently on a second break.";
        }

        entry.Break2EndAt = DateTime.UtcNow;
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
        if (IsOnBreak2(entry))
        {
            return "End your second break before clocking out.";
        }

        entry.ClockOutAt = DateTime.UtcNow;
        return null;
    });

    // Lets a Lead/Admin close out someone else's entry directly — e.g. they
    // left early, or forgot to clock out. Unlike the self clock-out above,
    // this skips the break/lunch-must-be-closed guard: it's exactly the
    // override for someone who left without going through the normal flow.
    [HttpPost("{id:int}/admin-clock-out")]
    [Authorize(Policy = "LeadOrAbove")]
    public async Task<ActionResult<TimeEntryDto>> AdminClockOut(int id, AdminClockOutRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Note))
        {
            return BadRequest("A note is required to clock someone out.");
        }

        var entry = db.TimeEntries
            .Include(t => t.ShiftAssignment).ThenInclude(a => a!.Shift).ThenInclude(s => s!.Location)
            .SingleOrDefault(t => t.Id == id);
        if (entry is null || !CanAccess(entry.ShiftAssignment?.Shift?.Location?.LocationCode))
        {
            return NotFound();
        }

        if (entry.ClockOutAt is not null)
        {
            return BadRequest("Already clocked out.");
        }

        entry.ClockOutAt = DateTime.UtcNow;
        entry.Note = request.Note;
        entry.ClockedOutByAccountId = CallerAccountId();
        db.SaveChanges();

        await notifier.NotifyLocationChanged(entry.ShiftAssignment!.Shift!.Location!.LocationCode);
        return Ok(ToDto(entry));
    }

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

    private static bool IsOnBreak2(TimeEntry t) => t.Break2StartAt is not null && t.Break2EndAt is null;

    private bool CanAccess(string? locationCode) =>
        User.IsInRole(nameof(AccountRole.Sa)) || (locationCode is not null && locationCode == CallerLocationCode());

    private string? CallerLocationCode() =>
        User.FindFirst(TokenService.LocationCodeClaimType)?.Value;

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
        t.Break2StartAt,
        t.Break2EndAt,
        t.ClockOutAt,
        t.ClockedOutByAccountId,
        t.Note);
}
