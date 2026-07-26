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

// Converts an employee's submitted availability into an actual schedule by
// assigning them to a Shift template on a specific date. Managing
// assignments is Admin/Sa-only (see the per-action policy below, except
// MarkAbsent which Leads can also do); any authenticated account can read
// back its own via GetMine.
[ApiController]
[Route("api/shift-assignments")]
[Authorize]
public class ShiftAssignmentsController(AppDbContext db, IScheduleNotifier notifier) : ControllerBase
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
            .Include(a => a.Shift).ThenInclude(s => s!.ScheduledBreaks)
            .Include(a => a.Account)
            .Where(a => a.AccountId == accountId && a.Date >= today && a.IsPublished)
            .OrderBy(a => a.Date)
            .ThenBy(a => a.Shift!.StartTime)
            .ThenBy(a => a.Account!.FirstName)
            .ThenBy(a => a.Account!.LastName)
            .ToList();

        var breakWindows = ComputeBreakWindows(assignments);
        return Ok(assignments.Select(a => ToDto(a, breakWindows)));
    }

    // Bulk-marks every assignment in a location/week as published so it
    // starts showing up in GetMine for the employees on it. Until this is
    // called, the admin schedule grid is a draft/preview only.
    [HttpPost("publish")]
    [Authorize(Policy = "AdminOrAbove")]
    public async Task<IActionResult> Publish(PublishScheduleRequest request)
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
        await notifier.NotifyLocationChanged(location.LocationCode);
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
            .Include(a => a.Shift).ThenInclude(s => s!.ScheduledBreaks)
            .Include(a => a.Account)
            .Where(a => a.Shift!.LocationId == location.Id && a.Date >= weekStartDate && a.Date <= weekEndDate)
            .OrderBy(a => a.Date)
            .ThenBy(a => a.Shift!.StartTime)
            .ThenBy(a => a.Account!.FirstName)
            .ThenBy(a => a.Account!.LastName)
            .ToList();

        var breakWindows = ComputeBreakWindows(assignments);
        return Ok(assignments.Select(a => ToDto(a, breakWindows)));
    }

    [HttpPost]
    [Authorize(Policy = "AdminOrAbove")]
    public async Task<ActionResult<ShiftAssignmentDto>> Create(CreateShiftAssignmentRequest request)
    {
        var shift = db.Shifts.Include(s => s.Location).Include(s => s.ScheduledBreaks).SingleOrDefault(s => s.Id == request.ShiftId);
        var account = db.Accounts.Find(request.AccountId);
        if (shift is null || account is null || !CanAccess(shift.Location?.LocationCode) || account.LocationId != shift.LocationId)
        {
            return BadRequest("Shift and employee must belong to the same location you manage.");
        }

        if (account.Role is not (AccountRole.Employee or AccountRole.Lead or AccountRole.Admin))
        {
            return BadRequest("Only employees, leads, and admins can be scheduled.");
        }

        if (!IsDevelopmentMode(shift.LocationId) && !IsAvailable(account.Id, request.Date))
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
        var breakWindows = ComputeBreakWindows([assignment]);
        await notifier.NotifyLocationChanged(shift.Location!.LocationCode);
        return Ok(ToDto(assignment, breakWindows));
    }

    [HttpPut("{id:int}/move")]
    [Authorize(Policy = "AdminOrAbove")]
    public async Task<ActionResult<ShiftAssignmentDto>> Move(int id, MoveShiftAssignmentRequest request)
    {
        var assignment = db.ShiftAssignments
            .Include(a => a.Shift).ThenInclude(s => s!.Location)
            .Include(a => a.Shift).ThenInclude(s => s!.ScheduledBreaks)
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

        if (!IsDevelopmentMode(assignment.Shift!.LocationId) && !IsAvailable(account.Id, request.Date))
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
        var breakWindows = ComputeBreakWindows([assignment]);
        await notifier.NotifyLocationChanged(assignment.Shift!.Location!.LocationCode);
        return Ok(ToDto(assignment, breakWindows));
    }

    [HttpDelete("{id:int}")]
    [Authorize(Policy = "AdminOrAbove")]
    public async Task<IActionResult> Delete(int id)
    {
        var assignment = db.ShiftAssignments
            .Include(a => a.Shift).ThenInclude(s => s!.Location)
            .SingleOrDefault(a => a.Id == id);
        if (assignment is null || !CanAccess(assignment.Shift?.Location?.LocationCode))
        {
            return NotFound();
        }

        var locationCode = assignment.Shift!.Location!.LocationCode;
        db.ShiftAssignments.Remove(assignment);
        db.SaveChanges();
        await notifier.NotifyLocationChanged(locationCode);
        return NoContent();
    }

    // Lets a Lead/Admin mark an employee absent for a shift they didn't
    // show up for (or clear a mistaken mark). Marking someone absent who
    // did clock in wipes that TimeEntry (and its segments, via cascade
    // delete) rather than being blocked by it — absent means the shift
    // didn't happen, so a stray punch (e.g. clocked in then never showed
    // up again) shouldn't be left dangling behind it.
    [HttpPut("{id:int}/absent")]
    [Authorize(Policy = "LeadOrAbove")]
    public async Task<ActionResult<ShiftAssignmentDto>> MarkAbsent(int id, MarkAbsentRequest request)
    {
        var assignment = db.ShiftAssignments
            .Include(a => a.Shift).ThenInclude(s => s!.Location)
            .Include(a => a.Shift).ThenInclude(s => s!.ScheduledBreaks)
            .Include(a => a.Account)
            .SingleOrDefault(a => a.Id == id);
        if (assignment is null || !CanAccess(assignment.Shift?.Location?.LocationCode))
        {
            return NotFound();
        }

        if (request.IsAbsent && string.IsNullOrWhiteSpace(request.Note))
        {
            return BadRequest("A note is required when marking an employee absent.");
        }

        if (request.IsAbsent)
        {
            var existingEntry = db.TimeEntries.SingleOrDefault(t => t.ShiftAssignmentId == assignment.Id);
            if (existingEntry is not null)
            {
                db.TimeEntries.Remove(existingEntry);
            }
        }

        assignment.IsAbsent = request.IsAbsent;
        assignment.AbsenceNote = request.IsAbsent ? request.Note : null;
        assignment.AbsentMarkedByAccountId = CallerAccountId();
        assignment.AbsentMarkedAt = DateTime.UtcNow;
        db.SaveChanges();

        var breakWindows = ComputeBreakWindows([assignment]);
        await notifier.NotifyLocationChanged(assignment.Shift!.Location!.LocationCode);
        return Ok(ToDto(assignment, breakWindows));
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
    // shift there — unless the location has Development Mode on, which
    // waives this check entirely (see IsDevelopmentMode).
    private bool IsAvailable(int accountId, DateOnly date) =>
        db.Availabilities
            .Where(a => a.AccountId == accountId)
            .SelectMany(a => a.Days)
            .Any(d => d.Date == date && d.IsAvailable);

    // Lets an admin assign shifts regardless of submitted availability,
    // for locations that opt into it via LocationSettings.
    private bool IsDevelopmentMode(int locationId) =>
        db.LocationSettings
            .Where(s => s.LocationId == locationId)
            .Select(s => (bool?)s.DevelopmentMode)
            .SingleOrDefault() ?? false;

    private bool CanAccess(string? locationCode) =>
        User.IsInRole(nameof(AccountRole.Sa)) || (locationCode is not null && locationCode == CallerLocationCode());

    private string? CallerLocationCode() =>
        User.FindFirst(TokenService.LocationCodeClaimType)?.Value;

    private int CallerAccountId() =>
        int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    // Lunch is unpaid, so it comes out of the shift's clock span; Break is
    // paid and stays in — same convention current-week-schedule.ts uses for
    // actual worked hours (see workedMinutes there), applied here to the
    // *scheduled* span instead of clocked-in/out times.
    private static double ComputeHours(TimeOnly start, TimeOnly end, IEnumerable<ScheduledBreak> scheduledBreaks)
    {
        var span = end - start;
        if (span < TimeSpan.Zero)
        {
            // Overnight shift (e.g. 22:00-06:00) crosses midnight.
            span += TimeSpan.FromDays(1);
        }

        foreach (var b in scheduledBreaks)
        {
            if (b.Kind != BreakKind.Lunch)
            {
                continue;
            }

            var lunchSpan = b.EndTime - b.StartTime;
            if (lunchSpan < TimeSpan.Zero)
            {
                lunchSpan += TimeSpan.FromDays(1);
            }

            span -= lunchSpan;
        }

        return Math.Round(span.TotalHours, 2);
    }

    // Computes an actual non-overlapping window for every ScheduledBreak on
    // every assignment sharing a location + date — deliberately *not*
    // scoped to a single Shift template: two employees on entirely
    // different shifts (e.g. a mid shift and a closing shift both covering
    // 3-4pm) collide just as badly if their templates happen to default to
    // the same break time, and the whole point (#30) is nobody's ever away
    // from the floor at the same moment as anyone else. Only breaks of the
    // same Kind push each other — a Break overlapping a different
    // employee's Lunch is fine, only same-kind overlaps are avoided.
    //
    // Greedy placement, processed in original-start-time order (earliest
    // CreatedAt/Id as a stable tiebreaker for two breaks that start
    // identically): each candidate keeps its template's own time unless
    // that overlaps an already-placed same-kind window, in which case it's
    // pushed to start right as the conflicting one ends — repeating against
    // the full placed set until clear, since one push can land it inside a
    // second window it didn't originally conflict with.
    //
    // Takes its own DB pass rather than trusting the caller's list to
    // contain every sibling — GetMine, for instance, only loads one
    // account's assignments, but staggering needs everyone sharing that
    // location + date.
    private Dictionary<(int AssignmentId, int ScheduledBreakId), (TimeOnly Start, TimeOnly End)> ComputeBreakWindows(
        IReadOnlyCollection<ShiftAssignment> assignments)
    {
        var keys = assignments.Select(a => (a.Shift!.LocationId, a.Date)).Distinct().ToList();
        if (keys.Count == 0)
        {
            return [];
        }

        var locationIds = keys.Select(k => k.LocationId).Distinct().ToList();
        var dates = keys.Select(k => k.Date).Distinct().ToList();

        var scope = db.ShiftAssignments
            .Include(a => a.Shift).ThenInclude(s => s!.ScheduledBreaks)
            .Where(a => dates.Contains(a.Date) && locationIds.Contains(a.Shift!.LocationId))
            .ToList()
            .Where(a => keys.Contains((a.Shift!.LocationId, a.Date)));

        var candidates = scope.SelectMany(a => a.Shift!.ScheduledBreaks.Select(b => new
        {
            AssignmentId = a.Id,
            ScheduledBreakId = b.Id,
            b.Kind,
            a.Date,
            LocationId = a.Shift!.LocationId,
            OriginalStart = b.StartTime,
            OriginalEnd = b.EndTime,
            a.CreatedAt,
        }));

        var result = new Dictionary<(int, int), (TimeOnly, TimeOnly)>();
        foreach (var group in candidates.GroupBy(c => (c.Kind, c.Date, c.LocationId)))
        {
            var placed = new List<(TimeOnly Start, TimeOnly End)>();
            var ordered = group
                .OrderBy(c => c.OriginalStart)
                .ThenBy(c => c.CreatedAt)
                .ThenBy(c => c.AssignmentId)
                .ThenBy(c => c.ScheduledBreakId);

            foreach (var candidate in ordered)
            {
                var duration = candidate.OriginalEnd - candidate.OriginalStart;
                if (duration <= TimeSpan.Zero)
                {
                    duration += TimeSpan.FromDays(1);
                }

                var start = candidate.OriginalStart;
                int conflictIndex;
                while ((conflictIndex = placed.FindIndex(p => start < p.End && p.Start < start.Add(duration))) != -1)
                {
                    start = placed[conflictIndex].End;
                }

                var end = start.Add(duration);
                placed.Add((start, end));
                result[(candidate.AssignmentId, candidate.ScheduledBreakId)] = (start, end);
            }
        }

        return result;
    }

    private static ShiftAssignmentDto ToDto(
        ShiftAssignment a,
        IReadOnlyDictionary<(int AssignmentId, int ScheduledBreakId), (TimeOnly Start, TimeOnly End)> breakWindows) => new(
        a.Id,
        a.ShiftId,
        a.Shift!.Name,
        a.Shift.StartTime,
        a.Shift.EndTime,
        a.Shift.ScheduledBreaks
            .OrderBy(b => b.StartTime)
            .Select(b =>
            {
                var (start, end) = breakWindows.TryGetValue((a.Id, b.Id), out var window)
                    ? window
                    : (b.StartTime, b.EndTime);
                return new ScheduledBreakDto(b.Kind, start, end);
            })
            .ToList(),
        ComputeHours(a.Shift.StartTime, a.Shift.EndTime, a.Shift.ScheduledBreaks),
        a.AccountId,
        a.Account!.FirstName,
        a.Account.LastName,
        a.Date,
        a.IsPublished,
        a.IsAbsent,
        a.AbsenceNote,
        a.AbsentMarkedByAccountId,
        a.AbsentMarkedAt);
}
