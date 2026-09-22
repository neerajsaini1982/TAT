using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Server.Data;
using Server.Dtos;
using Server.Hubs;
using Server.Models;
using Server.Security;
using Server.Services;

namespace Server.Controllers;

// Punch-clock entries. Self-service only for the employee's own punches: an
// account clocks itself in for one of its own published shifts, within a
// per-location window (LocationSettings.ClockInWindowMinutes) before that
// shift's start time, then moves through any number of break/lunch
// segments — at most one open (no EndAt) at a time — before clocking out.
// The actual state-transition rules live in TimeEntryPunchService, shared
// with KioskController's PIN-verified punches; this controller just derives
// accountId from the caller's own JWT and maps the result to an HTTP
// response. Leads/Admins get one override on top of that: AdminClockOut,
// for closing out an entry the employee didn't (see its doc comment).
[ApiController]
[Route("api/time-entries")]
[Authorize]
public class TimeEntriesController(AppDbContext db, IScheduleNotifier notifier, TimeEntryPunchService punchService) : ControllerBase
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

        return Ok(entries.Select(TimeEntryPunchService.ToDto));
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

        return Ok(entries.Select(TimeEntryPunchService.ToDto));
    }

    [HttpPost("clock-in")]
    public ActionResult<TimeEntryDto> ClockIn(ClockInRequest request) =>
        ToActionResult(punchService.ClockIn(CallerAccountId(), request.ShiftAssignmentId, ClientIp()));

    // Starts a new break/lunch segment — any number allowed per shift, but
    // only one open (no EndAt) at a time, enforced in TimeEntryPunchService.
    [HttpPost("{id:int}/segments/start")]
    public ActionResult<TimeEntryDto> StartSegment(int id, StartSegmentRequest request) =>
        ToActionResult(punchService.StartSegment(CallerAccountId(), id, request.Kind, ClientIp()));

    [HttpPost("{id:int}/segments/end")]
    public ActionResult<TimeEntryDto> EndSegment(int id) =>
        ToActionResult(punchService.EndSegment(CallerAccountId(), id, ClientIp()));

    [HttpPost("{id:int}/clock-out")]
    public ActionResult<TimeEntryDto> ClockOut(int id) =>
        ToActionResult(punchService.ClockOut(CallerAccountId(), id, ClientIp()));

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
        return Ok(TimeEntryPunchService.ToDto(entry));
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
        return Ok(TimeEntryPunchService.ToDto(entry));
    }

    // Lets a Lead/Admin flag (or clear) that an employee left before the end
    // of their shift, independent of whether the entry was closed by a self
    // clock-out or an AdminClockOut above — e.g. they clocked themselves out
    // normally but a supervisor later confirms it was early. Only valid once
    // the entry has been clocked out.
    [HttpPut("{id:int}/left-early")]
    [Authorize(Policy = "LeadOrAbove")]
    public async Task<ActionResult<TimeEntryDto>> MarkLeftEarly(int id, MarkLeftEarlyRequest request)
    {
        var entry = db.TimeEntries
            .Include(t => t.Segments)
            .Include(t => t.ShiftAssignment).ThenInclude(a => a!.Shift).ThenInclude(s => s!.Location)
            .SingleOrDefault(t => t.Id == id);
        if (entry is null || !CanAccess(entry.ShiftAssignment?.Shift?.Location?.LocationCode))
        {
            return NotFound();
        }

        if (entry.ClockOutAt is null)
        {
            return BadRequest("Can't mark left early before the employee has clocked out.");
        }

        if (request.LeftEarly && string.IsNullOrWhiteSpace(request.Note))
        {
            return BadRequest("A note is required when marking an employee as having left early.");
        }

        entry.LeftEarly = request.LeftEarly;
        entry.LeftEarlyNote = request.LeftEarly ? request.Note : null;
        entry.LeftEarlyMarkedByAccountId = CallerAccountId();
        entry.LeftEarlyMarkedAt = DateTime.UtcNow;
        db.SaveChanges();

        await notifier.NotifyLocationChanged(entry.ShiftAssignment!.Shift!.Location!.LocationCode);
        return Ok(TimeEntryPunchService.ToDto(entry));
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

    // Maps a TimeEntryPunchService result to the same HTTP shape the old
    // inline controller logic returned — same 404/400/409/403 semantics as
    // before the extraction, for both self-service and (via KioskController)
    // kiosk callers.
    private ActionResult<TimeEntryDto> ToActionResult(PunchResult result) => result.StatusCode switch
    {
        StatusCodes.Status200OK => Ok(result.Entry),
        StatusCodes.Status409Conflict => Conflict(result.Error),
        StatusCodes.Status403Forbidden => StatusCode(StatusCodes.Status403Forbidden, result.Error),
        StatusCodes.Status404NotFound => NotFound(),
        _ => BadRequest(result.Error),
    };

    // Prefers X-Forwarded-For (set by Azure App Service's front end) over
    // the socket-level RemoteIpAddress, which on Azure reflects the
    // internal proxy hop rather than the real client.
    private string? ClientIp()
    {
        var forwarded = Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwarded))
        {
            return forwarded.Split(',')[0].Trim();
        }
        return HttpContext.Connection.RemoteIpAddress?.ToString();
    }

    private bool CanAccess(string? locationCode) =>
        User.IsInRole(nameof(AccountRole.Sa)) || (locationCode is not null && locationCode == CallerLocationCode());

    private string? CallerLocationCode() =>
        User.FindFirst(TokenService.LocationCodeClaimType)?.Value;

    private int CallerAccountId() =>
        int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
