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
// shift's start time, then moves through any number of break/lunch
// segments — at most one open (no EndAt) at a time — before clocking out.
// Leads/Admins get one override on top of that: AdminClockOut, for closing
// out an entry the employee didn't (see its doc comment).
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
            .Include(t => t.Segments)
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
            .Include(t => t.Segments)
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

    // Starts a new break/lunch segment — any number allowed per shift, but
    // only one open (no EndAt) at a time, enforced below.
    [HttpPost("{id:int}/segments/start")]
    public ActionResult<TimeEntryDto> StartSegment(int id, StartSegmentRequest request) => Transition(id, entry =>
    {
        if (entry.ClockOutAt is not null)
        {
            return "Already clocked out.";
        }
        if (entry.Segments.Any(s => s.EndAt is null))
        {
            return "End your current break or lunch before starting another.";
        }

        entry.Segments.Add(new TimeEntrySegment { Kind = request.Kind, StartAt = DateTime.UtcNow });
        return null;
    });

    [HttpPost("{id:int}/segments/end")]
    public ActionResult<TimeEntryDto> EndSegment(int id) => Transition(id, entry =>
    {
        var open = entry.Segments.FirstOrDefault(s => s.EndAt is null);
        if (open is null)
        {
            return "Not currently on a break or lunch.";
        }

        open.EndAt = DateTime.UtcNow;
        return null;
    });

    [HttpPost("{id:int}/clock-out")]
    public ActionResult<TimeEntryDto> ClockOut(int id) => Transition(id, entry =>
    {
        if (entry.ClockOutAt is not null)
        {
            return "Already clocked out.";
        }
        if (entry.Segments.Any(s => s.EndAt is null))
        {
            return "End your current break or lunch before clocking out.";
        }

        entry.ClockOutAt = DateTime.UtcNow;
        return null;
    });

    // Lets a Lead/Admin close out someone else's entry directly — e.g. they
    // left early, or forgot to clock out. Unlike the self clock-out above,
    // this skips the open-segment guard: it's exactly the override for
    // someone who left without going through the normal flow.
    [HttpPost("{id:int}/admin-clock-out")]
    [Authorize(Policy = "LeadOrAbove")]
    public async Task<ActionResult<TimeEntryDto>> AdminClockOut(int id, AdminClockOutRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Note))
        {
            return BadRequest("A note is required to clock someone out.");
        }

        var entry = db.TimeEntries
            .Include(t => t.Segments)
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

    // Lets a Lead/Admin set every punch on a shift's TimeEntry directly,
    // instead of just closing it out (see AdminClockOut above) — e.g. the
    // employee clocked in but their break times are wrong, or they forgot
    // to clock in at all and the admin is filling in the whole shift after
    // the fact. Upserts by ShiftAssignmentId: creates the entry if none
    // exists yet, otherwise overwrites every punch (and wholesale-replaces
    // every segment) on the existing one.
    [HttpPut("by-assignment/{shiftAssignmentId:int}/admin-edit")]
    [Authorize(Policy = "LeadOrAbove")]
    public async Task<ActionResult<TimeEntryDto>> AdminEditTimes(int shiftAssignmentId, AdminEditTimeEntryRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Note))
        {
            return BadRequest("A note is required to edit punch times.");
        }

        var validationError = ValidateSegments(request.Segments, request.ClockInAt, request.ClockOutAt);
        if (validationError is not null)
        {
            return BadRequest(validationError);
        }

        var assignment = db.ShiftAssignments
            .Include(a => a.Shift).ThenInclude(s => s!.Location)
            .SingleOrDefault(a => a.Id == shiftAssignmentId);
        if (assignment is null || !CanAccess(assignment.Shift?.Location?.LocationCode))
        {
            return NotFound();
        }

        var entry = db.TimeEntries.Include(t => t.Segments).SingleOrDefault(t => t.ShiftAssignmentId == shiftAssignmentId);
        if (entry is null)
        {
            entry = new TimeEntry { AccountId = assignment.AccountId, ShiftAssignmentId = assignment.Id };
            db.TimeEntries.Add(entry);
        }

        entry.ClockInAt = request.ClockInAt;
        entry.ClockOutAt = request.ClockOutAt;
        entry.Note = request.Note;
        entry.ClockedOutByAccountId = request.ClockOutAt is not null ? CallerAccountId() : null;
        entry.EditedByAccountId = CallerAccountId();
        entry.EditedAt = DateTime.UtcNow;

        db.TimeEntrySegments.RemoveRange(entry.Segments);
        entry.Segments = request.Segments
            .Select(s => new TimeEntrySegment { Kind = s.Kind, StartAt = s.StartAt, EndAt = s.EndAt })
            .ToList();

        // A recorded clock-in supersedes an earlier absence mark, same as a
        // normal self clock-in.
        assignment.IsAbsent = false;
        assignment.AbsenceNote = null;

        db.SaveChanges();

        await notifier.NotifyLocationChanged(assignment.Shift!.Location!.LocationCode);
        return Ok(ToDto(entry));
    }

    // Sanity-checks segment ordering and overlap only — deliberately not
    // checked against the shift's scheduled window or clock-in/out range,
    // since correcting exactly that kind of mismatch is often the reason
    // for the edit.
    private static string? ValidateSegments(List<AdminSegmentInput> segments, DateTime clockInAt, DateTime? clockOutAt)
    {
        foreach (var s in segments)
        {
            if (s.EndAt is not null && s.EndAt < s.StartAt)
            {
                return $"{s.Kind} end can't be before its start.";
            }
        }

        var ordered = segments.OrderBy(s => s.StartAt).ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            for (var j = i + 1; j < ordered.Count; j++)
            {
                var a = ordered[i];
                var b = ordered[j];
                var aEnd = a.EndAt ?? DateTime.MaxValue;
                var bEnd = b.EndAt ?? DateTime.MaxValue;
                if (a.StartAt < bEnd && b.StartAt < aEnd)
                {
                    return "Breaks and lunches can't overlap.";
                }
            }
        }

        if (clockOutAt is not null && clockOutAt < clockInAt)
        {
            return "Clock out can't be before clock in.";
        }

        return null;
    }

    // Applies a state-transition function to the caller's own entry, saving
    // and returning the updated DTO on success, or the returned message as a
    // 400 when the transition isn't valid from the entry's current state.
    private ActionResult<TimeEntryDto> Transition(int id, Func<TimeEntry, string?> apply)
    {
        var entry = db.TimeEntries.Include(t => t.Segments).SingleOrDefault(t => t.Id == id);
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
        t.ClockOutAt,
        t.Segments
            .OrderBy(s => s.StartAt)
            .Select(s => new TimeEntrySegmentDto(s.Id, s.Kind, s.StartAt, s.EndAt))
            .ToList(),
        t.ClockedOutByAccountId,
        t.Note,
        t.EditedByAccountId,
        t.EditedAt);
}
