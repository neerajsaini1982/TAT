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

// Converts an employee's submitted availability into an actual schedule by
// assigning them to a Shift template on a specific date. Managing
// assignments is Admin/Sa-only (see the per-action policy below, except
// MarkAbsent and the call-out/cover actions, which Leads can also do); any
// authenticated account can read back its own via GetMine.
[ApiController]
[Route("api/shift-assignments")]
[Authorize]
public class ShiftAssignmentsController(AppDbContext db, IScheduleNotifier notifier, IEmailSender emailSender) : ControllerBase
{
    // Self-service: whoever is logged in sees their own upcoming schedule,
    // and everyone else's too if they've individually been granted the
    // Account.CanSeeAllSchedules override, or if their role has been granted
    // Schedule Visibility for this location (see LocationSettings/
    // CanSeeAllSchedules) — otherwise an Employee/Lead/Admin can still always
    // answer "when am I working next, and for how long" without needing the
    // admin roster view.
    [HttpGet("mine")]
    public ActionResult<IEnumerable<ShiftAssignmentDto>> GetMine()
    {
        var accountId = CallerAccountId();
        var today = DateOnly.FromDateTime(DateTime.Now);

        var location = db.Locations.SingleOrDefault(l => l.LocationCode == CallerLocationCode());
        var seeAllSchedules = location is not null && CanSeeAllSchedules(location.Id);

        var query = db.ShiftAssignments
            .Include(a => a.Shift).ThenInclude(s => s!.ScheduledBreaks)
            .Include(a => a.Account)
            .Where(a => a.Date >= today && a.IsPublished);
        query = seeAllSchedules
            ? query.Where(a => a.Shift!.LocationId == location!.Id)
            : query.Where(a => a.AccountId == accountId);

        var assignments = query
            .OrderBy(a => a.Date)
            .ThenBy(a => a.Shift!.StartTime)
            .ThenBy(a => a.Account!.FirstName)
            .ThenBy(a => a.Account!.LastName)
            .ToList();

        var breakWindows = ComputeBreakWindows(db, assignments);
        return Ok(assignments.Select(a => ToDto(a, breakWindows)));
    }

    // Bulk-marks every assignment in a location/week as published so it
    // starts showing up in GetMine for the employees on it. Until this is
    // called, the admin schedule grid is a draft/preview only. When the
    // admin opts in via the SendEmail checkbox, also emails every scheduled
    // employee using the SchedulePublished template — checked up front so a
    // missing SMTP setup is reported before anything is published, rather
    // than after.
    [HttpPost("publish")]
    [Authorize(Policy = "AdminOrAbove")]
    public async Task<IActionResult> Publish(PublishScheduleRequest request)
    {
        var location = ResolveLocation(request.LocationCode);
        if (location is null)
        {
            return BadRequest("A valid locationCode is required.");
        }

        LocationSettings? settings = null;
        if (request.SendEmail)
        {
            settings = db.LocationSettings.SingleOrDefault(s => s.LocationId == location.Id);
            if (settings is null || string.IsNullOrWhiteSpace(settings.SmtpHost))
            {
                return BadRequest("SMTP is not configured for this location. Set it up under Settings first.");
            }
        }

        var weekEndDate = request.WeekStartDate.AddDays(6);
        var assignments = db.ShiftAssignments
            .Include(a => a.Shift)
            .Include(a => a.Account)
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

        if (settings is not null)
        {
            await SendScheduleEmails(location, settings, assignments, request.WeekStartDate, weekEndDate);
        }

        return NoContent();
    }

    // Emails everyone with a shift this week once, even if they have
    // several assignments across the week. Sending is best-effort per
    // employee: one bad address or a transient SMTP hiccup shouldn't stop
    // the rest of the location from being notified (the schedule is already
    // published at this point regardless).
    private async Task SendScheduleEmails(
        Location location, LocationSettings settings, List<ShiftAssignment> assignments, DateOnly weekStartDate, DateOnly weekEndDate)
    {
        var template = db.EmailTemplates.SingleOrDefault(
            t => t.LocationId == location.Id && t.Key == EmailTemplateKeys.SchedulePublished)
            ?? EmailTemplateCatalog.Default(EmailTemplateKeys.SchedulePublished);

        var weekRange = $"{weekStartDate.ToString("MMM d")} – {weekEndDate.ToString("MMM d")}";

        var employees = assignments
            .Select(a => a.Account!)
            .Where(a => !string.IsNullOrWhiteSpace(a.Email))
            .DistinctBy(a => a.Id);

        foreach (var employee in employees)
        {
            var placeholders = new Dictionary<string, string>
            {
                ["{{employeeName}}"] = $"{employee.FirstName} {employee.LastName}",
                ["{{locationName}}"] = location.Name,
                ["{{weekRange}}"] = weekRange,
                ["{{schedule}}"] = BuildScheduleHtml(assignments.Where(a => a.AccountId == employee.Id), settings.TimeFormat),
            };

            try
            {
                await emailSender.SendAsync(
                    settings,
                    employee.Email,
                    EmailTemplateCatalog.Render(template.Subject, placeholders),
                    EmailTemplateCatalog.Render(template.BodyHtml, placeholders));
            }
            catch
            {
                // Best-effort — one employee's bad address/SMTP hiccup
                // shouldn't stop the rest of the location from being notified.
            }
        }
    }

    // Renders one employee's shifts for the week as an inline HTML table
    // (email clients don't run CSS files, so styling is inline) — this is
    // what the {{schedule}} placeholder expands to in SendScheduleEmails.
    internal static string BuildScheduleHtml(IEnumerable<ShiftAssignment> employeeAssignments, TimeFormat timeFormat)
    {
        const string cell = "padding:4px 12px;border:1px solid #ddd;text-align:left;";

        var rows = employeeAssignments
            .OrderBy(a => a.Date)
            .ThenBy(a => a.Shift!.StartTime)
            .Select(a =>
                $"<tr><td style=\"{cell}\">{a.Date:ddd, MMM d}</td>"
                + $"<td style=\"{cell}\">{FormatTime(a.Shift!.StartTime, timeFormat)}–{FormatTime(a.Shift!.EndTime, timeFormat)}</td>"
                + $"<td style=\"{cell}\">{a.Shift!.Name}</td></tr>");

        return "<table style=\"border-collapse:collapse;margin-top:8px;\">"
            + $"<tr><th style=\"{cell}\">Date</th><th style=\"{cell}\">Time</th><th style=\"{cell}\">Shift</th></tr>"
            + string.Concat(rows)
            + "</table>";
    }

    private static string FormatTime(TimeOnly time, TimeFormat timeFormat) =>
        time.ToString(timeFormat == TimeFormat.TwelveHour ? "h:mm tt" : "HH:mm");

    internal static string FormatTimeRange(Shift shift, TimeFormat timeFormat) =>
        $"{FormatTime(shift.StartTime, timeFormat)}–{FormatTime(shift.EndTime, timeFormat)}";

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

        var breakWindows = ComputeBreakWindows(db, assignments);
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
            a.AccountId == account.Id && a.Date == request.Date);
        if (alreadyAssigned)
        {
            return Conflict("This employee is already assigned a shift on this date.");
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
        var breakWindows = ComputeBreakWindows(db, [assignment]);
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
            a.Id != assignment.Id && a.AccountId == account.Id && a.Date == request.Date);
        if (alreadyAssigned)
        {
            return Conflict("This employee is already assigned a shift on this date.");
        }

        assignment.AccountId = account.Id;
        assignment.Date = request.Date;
        db.SaveChanges();

        assignment.Account = account;
        var breakWindows = ComputeBreakWindows(db, [assignment]);
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

        var breakWindows = ComputeBreakWindows(db, [assignment]);
        await notifier.NotifyLocationChanged(assignment.Shift!.Location!.LocationCode);
        return Ok(ToDto(assignment, breakWindows));
    }

    // Lets an Admin/Sa record sick hours an employee reported for a shift
    // (typically texted in, not clocked) — see SetSickMinutesRequest. Kept
    // independent of MarkAbsent: entering sick hours doesn't itself flip
    // IsAbsent, since an admin may want both, either, or neither depending
    // on how the day is being tracked.
    [HttpPut("{id:int}/sick-hours")]
    [Authorize(Policy = "AdminOrAbove")]
    public async Task<ActionResult<ShiftAssignmentDto>> SetSickMinutes(int id, SetSickMinutesRequest request)
    {
        if (request.SickMinutes < 0)
        {
            return BadRequest("Sick minutes can't be negative.");
        }

        var assignment = db.ShiftAssignments
            .Include(a => a.Shift).ThenInclude(s => s!.Location)
            .Include(a => a.Shift).ThenInclude(s => s!.ScheduledBreaks)
            .Include(a => a.Account)
            .SingleOrDefault(a => a.Id == id);
        if (assignment is null || !CanAccess(assignment.Shift?.Location?.LocationCode))
        {
            return NotFound();
        }

        assignment.SickMinutes = request.SickMinutes;
        assignment.SickHoursRecordedByAccountId = CallerAccountId();
        assignment.SickHoursRecordedAt = DateTime.UtcNow;
        db.SaveChanges();

        var breakWindows = ComputeBreakWindows(db, [assignment]);
        await notifier.NotifyLocationChanged(assignment.Shift!.Location!.LocationCode);
        return Ok(ToDto(assignment, breakWindows));
    }

    // Everyone at the location who could cover this assignment's shift, for
    // the call-out dialog's picker. Unavailable employees and ones already
    // working that day are included with the facts the picker warns about —
    // for the latter, the single combined shift they'd be given.
    [HttpGet("{id:int}/cover-candidates")]
    [Authorize(Policy = "LeadOrAbove")]
    public ActionResult<CoverCandidatesDto> GetCoverCandidates(int id, [FromQuery] int? shiftId = null)
    {
        var assignment = LoadForCover(id);
        if (assignment is null)
        {
            return NotFound();
        }

        var shift = assignment.Shift!;
        var activeShifts = db.Shifts
            .Include(s => s.ScheduledBreaks)
            .Where(s => s.LocationId == shift.LocationId && s.IsActive)
            .ToList();
        // The replacement shift to preview for people already working that
        // day, instead of the automatic combined one.
        var chosen = activeShifts.SingleOrDefault(s => s.Id == shiftId);
        if (shiftId is not null && chosen is null)
        {
            return BadRequest(InvalidCoverShiftMessage);
        }

        var settings = db.LocationSettings.SingleOrDefault(s => s.LocationId == shift.LocationId);
        var overtimePolicy = (settings ?? new LocationSettings()).GetOvertimePolicy();
        var (weekStart, weekEnd) = OvertimeCalculator.WorkweekSpan(assignment.Date, assignment.Date, overtimePolicy.WorkweekStartDay);
        var existingCover = db.ShiftAssignments.SingleOrDefault(a => a.CoversAssignmentId == assignment.Id);

        var weekAssignments = db.ShiftAssignments
            .Include(a => a.Shift).ThenInclude(s => s!.ScheduledBreaks)
            .Include(a => a.OriginalShift).ThenInclude(s => s!.ScheduledBreaks)
            .Where(a => a.Shift!.LocationId == shift.LocationId && a.Date >= weekStart && a.Date <= weekEnd)
            .ToList()
            .Where(a => a.Id != assignment.Id)
            .ToLookup(a => a.AccountId);

        var clockedOutAssignmentIds = db.TimeEntries
            .Where(t => t.ShiftAssignment!.Date == assignment.Date && t.ClockOutAt != null)
            .Select(t => t.ShiftAssignmentId)
            .ToHashSet();

        var availableAccountIds = db.AvailabilityDays
            .Where(d => d.Date == assignment.Date && d.IsAvailable)
            .Select(d => d.Availability!.AccountId)
            .ToHashSet();
        var developmentMode = settings?.DevelopmentMode ?? false;

        var candidates = db.Accounts
            .Where(a => a.LocationId == shift.LocationId && a.IsActive && a.IsOnShiftSchedule && a.Id != assignment.AccountId)
            .ToList()
            .Where(a => a.Role is AccountRole.Employee or AccountRole.Lead or AccountRole.Admin)
            .Select(account => new
            {
                account,
                week = weekAssignments[account.Id].ToList(),
                sameDay = weekAssignments[account.Id].Where(a => a.Date == assignment.Date).ToList(),
            })
            // Someone who called out themselves that day isn't offered.
            .Where(c => !c.sameDay.Any(a => a.IsAbsent))
            .Select(c =>
            {
                var isCurrentCover = existingCover is not null && existingCover.AccountId == c.account.Id;
                var own = c.sameDay.Count == 1 ? c.sameDay[0] : null;

                // What their day turns into. Someone with the day off works
                // the absent shift; someone already working has their own
                // shift swapped for the chosen one, or by default for one
                // combining both. The current cover's own shift is the one
                // they had before, and their swap is already in place.
                var ownShift = isCurrentCover ? own?.OriginalShift : own?.Shift;
                var blockedReason = isCurrentCover ? null : CombineBlockedReason(c.sameDay, shift, clockedOutAssignmentIds);
                var automatic = ownShift is null || blockedReason is not null ? null : BuildCombinedShift(ownShift, shift);
                var combined = automatic is null ? null : chosen ?? (isCurrentCover ? own!.Shift! : automatic);
                var dayShift = combined ?? (isCurrentCover && own is not null ? own.Shift! : shift);

                var scheduled = c.week
                    .Where(a => !a.IsAbsent && a.Date != assignment.Date)
                    .Select(a => new DayHours(a.Date, ReportsController.ScheduledMinutesFor(a.Shift!)))
                    .Append(new DayHours(assignment.Date, ReportsController.ScheduledMinutesFor(dayShift)))
                    .ToList();

                var overtimeMinutes = c.account.IsOvertimeExempt
                    ? 0
                    : OvertimeCalculator.Calculate(overtimePolicy, scheduled).Sum(d => d.OvertimeMinutes + d.DoubleTimeMinutes);

                return new CoverCandidateDto(
                    c.account.Id,
                    c.account.FirstName,
                    c.account.LastName,
                    developmentMode || availableAccountIds.Contains(c.account.Id),
                    !string.IsNullOrWhiteSpace(c.account.Email),
                    Math.Round(scheduled.Sum(d => d.NetMinutes) / 60.0, 2),
                    overtimeMinutes,
                    ownShift is null ? null : Preview(ownShift),
                    combined is null ? null : Preview(combined),
                    ownShift is null || chosen is not null ? 0 : GapMinutes(ownShift, shift),
                    automatic is null
                        ? null
                        : activeShifts
                            .Where(s => s.StartTime == automatic.StartTime && s.EndTime == automatic.EndTime)
                            .OrderBy(s => s.Id)
                            .Select(s => (int?)s.Id)
                            .FirstOrDefault(),
                    blockedReason);
            })
            .OrderBy(c => c.FirstName)
            .ThenBy(c => c.LastName)
            .ToList();

        return Ok(new CoverCandidatesDto(
            !string.IsNullOrWhiteSpace(settings?.SmtpHost),
            existingCover?.AccountId,
            existingCover?.OriginalShiftId is not null && activeShifts.Any(s => s.Id == existingCover.ShiftId)
                ? existingCover.ShiftId
                : null,
            activeShifts
                .OrderBy(s => s.StartTime)
                .ThenBy(s => s.EndTime)
                .ThenBy(s => s.Name)
                .Select(s => new CoverShiftOptionDto(s.Id, s.Name, s.StartTime, s.EndTime))
                .ToList(),
            candidates));
    }

    private static CoverShiftPreviewDto Preview(Shift shift) => new(
        shift.Name,
        shift.StartTime,
        shift.EndTime,
        shift.ScheduledBreaks.OrderBy(b => b.StartTime).Select(b => new ScheduledBreakDto(b.Kind, b.StartTime, b.EndTime)).ToList(),
        ComputeHours(shift.StartTime, shift.EndTime, shift.ScheduledBreaks));

    // The one-step call-out: marks the employee absent (as MarkAbsent does),
    // optionally records their sick hours (as SetSickMinutes does, admins
    // only) and optionally puts someone else on the shift. Everything is
    // validated before anything changes and saved in one go, so a cover
    // employee who can't take the shift leaves the absence unrecorded too.
    [HttpPut("{id:int}/call-out")]
    [Authorize(Policy = "LeadOrAbove")]
    public async Task<ActionResult<CallOutResultDto>> CallOut(int id, CallOutRequest request)
    {
        var assignment = LoadForCover(id);
        if (assignment is null)
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(request.Note))
        {
            return BadRequest("A note is required when marking an employee absent.");
        }

        if (request.SickMinutes is not null)
        {
            if (!User.IsInRole(nameof(AccountRole.Sa)) && !User.IsInRole(nameof(AccountRole.Admin)))
            {
                return Forbid();
            }

            if (request.SickMinutes < 0)
            {
                return BadRequest("Sick minutes can't be negative.");
            }
        }

        ShiftAssignment? cover = null;
        if (request.CoverAccountId is { } coverAccountId)
        {
            var (planned, error) = PlanCover(
                assignment, coverAccountId, request.CoverShiftId, request.ConfirmUnavailable, request.ConfirmCombine);
            if (error is not null)
            {
                return error;
            }

            cover = planned;
        }

        var existingEntry = db.TimeEntries.SingleOrDefault(t => t.ShiftAssignmentId == assignment.Id);
        if (existingEntry is not null)
        {
            db.TimeEntries.Remove(existingEntry);
        }

        var now = DateTime.UtcNow;
        assignment.IsAbsent = true;
        assignment.AbsenceNote = request.Note;
        assignment.AbsentMarkedByAccountId = CallerAccountId();
        assignment.AbsentMarkedAt = now;

        if (request.SickMinutes is { } sickMinutes)
        {
            assignment.SickMinutes = sickMinutes;
            assignment.SickHoursRecordedByAccountId = CallerAccountId();
            assignment.SickHoursRecordedAt = now;
        }

        db.SaveChanges();
        return Ok(await CallOutResult(assignment, cover, request.SendEmail));
    }

    // Assigns cover on a shift that's already marked absent ("Find Cover"),
    // or swaps the cover employee for someone else.
    [HttpPut("{id:int}/cover")]
    [Authorize(Policy = "LeadOrAbove")]
    public async Task<ActionResult<CallOutResultDto>> AssignCover(int id, AssignCoverRequest request)
    {
        var assignment = LoadForCover(id);
        if (assignment is null)
        {
            return NotFound();
        }

        if (!assignment.IsAbsent)
        {
            return BadRequest("Mark the employee absent before assigning cover.");
        }

        var (cover, error) = PlanCover(
            assignment, request.CoverAccountId, request.CoverShiftId, request.ConfirmUnavailable, request.ConfirmCombine);
        if (error is not null)
        {
            return error;
        }

        db.SaveChanges();
        return Ok(await CallOutResult(assignment, cover, request.SendEmail));
    }

    // Takes the cover employee back off an absent shift: someone who had the
    // day off loses the shift (refused once they've clocked in — their
    // punches belong to it); someone whose own shift was combined with it
    // goes back to the shift they had.
    [HttpDelete("{id:int}/cover")]
    [Authorize(Policy = "LeadOrAbove")]
    public async Task<ActionResult<ShiftAssignmentDto>> RemoveCover(int id)
    {
        var assignment = LoadForCover(id);
        if (assignment is null)
        {
            return NotFound();
        }

        var cover = db.ShiftAssignments.SingleOrDefault(a => a.CoversAssignmentId == assignment.Id);
        if (cover is not null)
        {
            var error = ReleaseCover(cover);
            if (error is not null)
            {
                return error;
            }

            db.SaveChanges();
        }

        var breakWindows = ComputeBreakWindows(db, [assignment]);
        await notifier.NotifyLocationChanged(assignment.Shift!.Location!.LocationCode);
        return Ok(ToDto(assignment, breakWindows));
    }

    private const string InvalidCoverShiftMessage = "Pick one of this location's active shifts.";

    private ShiftAssignment? LoadForCover(int id)
    {
        var assignment = db.ShiftAssignments
            .Include(a => a.Shift).ThenInclude(s => s!.Location)
            .Include(a => a.Shift).ThenInclude(s => s!.ScheduledBreaks)
            .Include(a => a.Account)
            .SingleOrDefault(a => a.Id == id);
        return assignment is null || !CanAccess(assignment.Shift?.Location?.LocationCode) ? null : assignment;
    }

    // Checks that coverAccountId can cover the absent assignment and stages
    // the change on the DbContext without saving. Returns an error result
    // and stages nothing when a check fails.
    //
    // Someone with the day off gets a new assignment on the absent shift.
    // Someone already working that day keeps their one assignment, switched
    // to a single shift for the whole day — one clock-in, one lunch, and any
    // punches they already have carry over. That shift is coverShiftId (one
    // of the location's own, picked the way the schedule builder picks one)
    // or, when none is given, a combined shift spanning their own and the
    // absent one (see BuildCombinedShift).
    //
    // Looser than Create on purpose, for cover only: being unavailable that
    // day or already having a shift on it are warnings the caller has to
    // confirm rather than hard blocks — last-minute cover usually comes from
    // exactly those people.
    private (ShiftAssignment? Cover, ActionResult? Error) PlanCover(
        ShiftAssignment absent, int coverAccountId, int? coverShiftId, bool confirmUnavailable, bool confirmCombine)
    {
        var shift = absent.Shift!;
        Shift? chosen = null;
        if (coverShiftId is not null)
        {
            chosen = db.Shifts.Include(s => s.ScheduledBreaks)
                .SingleOrDefault(s => s.Id == coverShiftId && s.LocationId == shift.LocationId && s.IsActive);
            if (chosen is null)
            {
                return (null, BadRequest(InvalidCoverShiftMessage));
            }
        }

        var account = db.Accounts.Find(coverAccountId);
        if (account is null || account.LocationId != shift.LocationId)
        {
            return (null, BadRequest("The employee must belong to the same location as the shift."));
        }

        if (account.Id == absent.AccountId)
        {
            return (null, BadRequest("An employee can't cover their own shift."));
        }

        if (!account.IsActive || account.Role is not (AccountRole.Employee or AccountRole.Lead or AccountRole.Admin))
        {
            return (null, BadRequest("Only active employees, leads, and admins can be scheduled."));
        }

        var existingCover = db.ShiftAssignments.SingleOrDefault(a => a.CoversAssignmentId == absent.Id);
        if (existingCover is not null && existingCover.AccountId == account.Id)
        {
            // Already covering: only the shift they were moved onto can change.
            if (chosen is not null && existingCover.OriginalShiftId is not null)
            {
                existingCover.Shift = chosen;
            }
            else
            {
                db.Entry(existingCover).Reference(a => a.Shift).Query().Include(s => s.ScheduledBreaks).Load();
            }

            existingCover.Account = account;
            return (existingCover, null);
        }

        var sameDay = db.ShiftAssignments
            .Include(a => a.Shift).ThenInclude(s => s!.ScheduledBreaks)
            .Where(a => a.AccountId == account.Id && a.Date == absent.Date)
            .ToList();
        if (sameDay.Any(a => a.IsAbsent))
        {
            return (null, BadRequest("This employee is marked absent on this date."));
        }

        var sameDayIds = sameDay.Select(a => a.Id).ToList();
        var clockedOutIds = db.TimeEntries
            .Where(t => sameDayIds.Contains(t.ShiftAssignmentId) && t.ClockOutAt != null)
            .Select(t => t.ShiftAssignmentId)
            .ToHashSet();
        if (CombineBlockedReason(sameDay, shift, clockedOutIds) is { } blockedReason)
        {
            return (null, Conflict(blockedReason));
        }

        var own = sameDay.SingleOrDefault();
        if (own is not null && !confirmCombine)
        {
            return (null, Conflict(
                "This employee already has a shift on this date. Confirm to replace it with the new shift."));
        }

        if (!confirmUnavailable && !IsDevelopmentMode(shift.LocationId) && !IsAvailable(account.Id, absent.Date))
        {
            return (null, Conflict("This employee is not available on this date. Confirm to assign them anyway."));
        }

        if (existingCover is not null && ReleaseCover(existingCover) is { } releaseError)
        {
            return (null, releaseError);
        }

        // Live straight away when the shift it covers is already posted, so
        // the cover employee can clock in without the week being reposted.
        var now = DateTime.UtcNow;
        ShiftAssignment cover;
        if (own is not null)
        {
            cover = own;
            cover.OriginalShiftId = own.ShiftId;
            cover.Shift = chosen ?? FindOrCreateCombinedShift(own.Shift!, shift);
            if (absent.IsPublished && !cover.IsPublished)
            {
                cover.IsPublished = true;
                cover.PublishedAt = now;
            }
        }
        else
        {
            cover = new ShiftAssignment
            {
                ShiftId = shift.Id,
                Shift = shift,
                AccountId = account.Id,
                Date = absent.Date,
                IsPublished = absent.IsPublished,
                PublishedAt = absent.IsPublished ? now : null,
            };
            db.ShiftAssignments.Add(cover);
        }

        cover.Account = account;
        cover.CoversAssignment = absent;
        cover.CoverAssignedByAccountId = CallerAccountId();
        cover.CoverAssignedAt = now;
        return (cover, null);
    }

    // Stages undoing a cover assignment: back to the shift the employee had
    // before it was combined, or gone altogether if covering was their only
    // shift that day — which is refused once they've clocked in.
    private ActionResult? ReleaseCover(ShiftAssignment cover)
    {
        if (cover.OriginalShiftId is { } originalShiftId)
        {
            cover.ShiftId = originalShiftId;
            cover.OriginalShiftId = null;
            cover.CoversAssignmentId = null;
            cover.CoverAssignedByAccountId = null;
            cover.CoverAssignedAt = null;
            return null;
        }

        if (db.TimeEntries.Any(t => t.ShiftAssignmentId == cover.Id))
        {
            return Conflict("The employee covering this shift has already clocked in, so they can't be taken off it.");
        }

        db.ShiftAssignments.Remove(cover);
        return null;
    }

    // Why someone already scheduled that day can't have their shift combined
    // with coverShift, or null when they can (including when they have no
    // shift that day at all).
    private static string? CombineBlockedReason(
        IReadOnlyList<ShiftAssignment> sameDay, Shift coverShift, IReadOnlySet<int> clockedOutAssignmentIds)
    {
        if (sameDay.Count == 0)
        {
            return null;
        }

        if (sameDay.Count > 1)
        {
            return "They already have more than one shift that day.";
        }

        var own = sameDay[0];
        if (own.CoversAssignmentId is not null)
        {
            return "They are already covering another shift that day.";
        }

        if (clockedOutAssignmentIds.Contains(own.Id))
        {
            return "They have already clocked out for the day.";
        }

        var (start, end) = CombinedSpan(own.Shift!, coverShift);
        return end - start >= 24 * 60 ? "The two shifts together would run a full day or longer." : null;
    }

    // The single shift someone works when they cover a shift on a day they
    // already have one: from the earlier start to the later end, taking in
    // any gap between the two (and any overlap just once). It keeps their
    // own shift's lunch — or the covered shift's if theirs has none — so
    // there's one lunch, not two, plus the short breaks from both.
    public static Shift BuildCombinedShift(Shift own, Shift cover)
    {
        var (start, end) = CombinedSpan(own, cover);
        var ordered = new[] { own, cover }.OrderBy(s => MinuteSpan(s).Start).ToList();

        var lunches = own.ScheduledBreaks.Where(b => b.Kind == BreakKind.Lunch).ToList();
        if (lunches.Count == 0)
        {
            lunches = cover.ScheduledBreaks.Where(b => b.Kind == BreakKind.Lunch).ToList();
        }

        var windows = lunches.Select(b => (b.Kind, b.StartTime, b.EndTime)).ToList();
        foreach (var b in ordered.SelectMany(s => s.ScheduledBreaks).Where(b => b.Kind == BreakKind.Break))
        {
            // Windows on one shift can't overlap (see ShiftsController), so a
            // break that collides with something already kept is dropped.
            if (!windows.Any(w => b.StartTime < w.EndTime && w.StartTime < b.EndTime))
            {
                windows.Add((b.Kind, b.StartTime, b.EndTime));
            }
        }

        return new Shift
        {
            Name = $"{ordered[0].Name} + {ordered[1].Name}",
            StartTime = TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(start)),
            EndTime = TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(end % (24 * 60))),
            LocationId = own.LocationId,
            // Not a template anyone picks from — kept out of the schedule
            // builder's shift list, which only offers active shifts.
            IsActive = false,
            ScheduledBreaks = windows
                .OrderBy(w => w.StartTime)
                .Select(w => new ScheduledBreak { Kind = w.Kind, StartTime = w.StartTime, EndTime = w.EndTime })
                .ToList(),
        };
    }

    // Reuses an identical combined shift from an earlier call-out rather
    // than adding a new row every time the same two shifts are joined.
    private Shift FindOrCreateCombinedShift(Shift own, Shift cover)
    {
        var combined = BuildCombinedShift(own, cover);
        static IEnumerable<(BreakKind, TimeOnly, TimeOnly)> Windows(Shift s) =>
            s.ScheduledBreaks.Select(b => (b.Kind, b.StartTime, b.EndTime)).Order();

        var existing = db.Shifts
            .Include(s => s.ScheduledBreaks)
            .Where(s => s.LocationId == combined.LocationId && !s.IsActive && s.Name == combined.Name
                && s.StartTime == combined.StartTime && s.EndTime == combined.EndTime)
            .ToList()
            .FirstOrDefault(s => Windows(s).SequenceEqual(Windows(combined)));
        if (existing is not null)
        {
            return existing;
        }

        db.Shifts.Add(combined);
        return combined;
    }

    // Minutes from midnight; an overnight shift ends past 24:00.
    private static (int Start, int End) MinuteSpan(Shift shift)
    {
        var start = shift.StartTime.Hour * 60 + shift.StartTime.Minute;
        var end = shift.EndTime.Hour * 60 + shift.EndTime.Minute;
        return (start, end > start ? end : end + 24 * 60);
    }

    private static (int Start, int End) CombinedSpan(Shift a, Shift b)
    {
        var (aStart, aEnd) = MinuteSpan(a);
        var (bStart, bEnd) = MinuteSpan(b);
        return (Math.Min(aStart, bStart), Math.Max(aEnd, bEnd));
    }

    // Time between two shifts on the same date that a combined shift takes
    // in; 0 when they touch or overlap.
    public static int GapMinutes(Shift a, Shift b)
    {
        var (aStart, aEnd) = MinuteSpan(a);
        var (bStart, bEnd) = MinuteSpan(b);
        return Math.Max(0, Math.Max(aStart, bStart) - Math.Min(aEnd, bEnd));
    }

    private async Task<CallOutResultDto> CallOutResult(ShiftAssignment absent, ShiftAssignment? cover, bool sendEmail)
    {
        var location = absent.Shift!.Location!;
        var emailSent = cover is not null && sendEmail && await SendCoverEmail(location, absent, cover);

        var breakWindows = ComputeBreakWindows(db, cover is null ? [absent] : [absent, cover]);
        await notifier.NotifyLocationChanged(location.LocationCode);
        return new CallOutResultDto(
            ToDto(absent, breakWindows),
            cover is null ? null : ToDto(cover, breakWindows),
            emailSent);
    }

    // Best-effort, like SendScheduleEmails: the cover assignment is already
    // saved, so a missing SMTP setup, a blank address or a send failure just
    // reports back as "not sent".
    private async Task<bool> SendCoverEmail(Location location, ShiftAssignment absent, ShiftAssignment cover)
    {
        var settings = db.LocationSettings.SingleOrDefault(s => s.LocationId == location.Id);
        var employee = cover.Account!;
        if (settings is null || string.IsNullOrWhiteSpace(settings.SmtpHost) || string.IsNullOrWhiteSpace(employee.Email))
        {
            return false;
        }

        var template = db.EmailTemplates.SingleOrDefault(
            t => t.LocationId == location.Id && t.Key == EmailTemplateKeys.CoverShiftAssigned)
            ?? EmailTemplateCatalog.Default(EmailTemplateKeys.CoverShiftAssigned);

        var placeholders = new Dictionary<string, string>
        {
            ["{{employeeName}}"] = $"{employee.FirstName} {employee.LastName}",
            ["{{locationName}}"] = location.Name,
            ["{{coveringFor}}"] = $"{absent.Account!.FirstName} {absent.Account.LastName}",
            ["{{shiftDate}}"] = cover.Date.ToString("ddd, MMM d"),
            ["{{shiftName}}"] = cover.Shift!.Name,
            ["{{shiftTime}}"] = FormatTimeRange(cover.Shift, settings.TimeFormat),
        };

        try
        {
            await emailSender.SendAsync(
                settings,
                employee.Email,
                EmailTemplateCatalog.Render(template.Subject, placeholders),
                EmailTemplateCatalog.Render(template.BodyHtml, placeholders));
            return true;
        }
        catch
        {
            return false;
        }
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

    // Resolves whether the caller can see everyone's shifts, not just their
    // own. A per-employee override (Account.CanSeeAllSchedules) wins outright
    // — an admin can grant this to one specific person regardless of their
    // role or the location's settings. Otherwise falls back to the Schedule
    // Visibility setting against the caller's own role: disabled entirely (or
    // no settings row yet) means everyone else is restricted to their own
    // shifts regardless of the per-role flags.
    private bool CanSeeAllSchedules(int locationId)
    {
        var callerOverride = db.Accounts
            .Where(a => a.Id == CallerAccountId())
            .Select(a => (bool?)a.CanSeeAllSchedules)
            .SingleOrDefault() ?? false;
        if (callerOverride)
        {
            return true;
        }

        var settings = db.LocationSettings.SingleOrDefault(s => s.LocationId == locationId);
        if (settings is null || !settings.ScheduleVisibilityEnabled)
        {
            return false;
        }

        if (User.IsInRole(nameof(AccountRole.Admin))) return settings.AdminSeesAllSchedules;
        if (User.IsInRole(nameof(AccountRole.Lead))) return settings.LeadSeesAllSchedules;
        if (User.IsInRole(nameof(AccountRole.Employee))) return settings.EmployeeSeesAllSchedules;
        return false;
    }

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
    internal static double ComputeHours(TimeOnly start, TimeOnly end, IEnumerable<ScheduledBreak> scheduledBreaks)
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
    //
    // That same pass is what lets ToDto name the other side of a cover
    // pairing: an absent assignment and its cover always share a location +
    // date, so loading every sibling here (with its Account and
    // OriginalShift) leaves CoversAssignment/CoveredByAssignment populated
    // on the caller's tracked entities.
    internal static Dictionary<(int AssignmentId, int ScheduledBreakId), (TimeOnly Start, TimeOnly End)> ComputeBreakWindows(
        AppDbContext db,
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
            .Include(a => a.Account)
            .Include(a => a.OriginalShift)
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

    internal static ShiftAssignmentDto ToDto(
        ShiftAssignment a,
        IReadOnlyDictionary<(int AssignmentId, int ScheduledBreakId), (TimeOnly Start, TimeOnly End)> breakWindows) => new(
        a.Id,
        a.ShiftId,
        a.Shift!.Name,
        a.Shift.StartTime,
        a.Shift.EndTime,
        BreakDtos(a, breakWindows),
        ComputeHours(a.Shift.StartTime, a.Shift.EndTime, a.Shift.ScheduledBreaks),
        a.AccountId,
        a.Account!.FirstName,
        a.Account.LastName,
        a.Date,
        a.IsPublished,
        a.IsAbsent,
        a.AbsenceNote,
        a.AbsentMarkedByAccountId,
        a.AbsentMarkedAt,
        a.SickMinutes,
        a.SickHoursRecordedByAccountId,
        a.SickHoursRecordedAt,
        a.CoversAssignmentId,
        a.CoversAssignment?.Account?.FirstName,
        a.CoversAssignment?.Account?.LastName,
        a.CoveredByAssignment?.Id,
        a.CoveredByAssignment?.Account?.FirstName,
        a.CoveredByAssignment?.Account?.LastName,
        a.OriginalShift?.Name);

    private static List<ScheduledBreakDto> BreakDtos(
        ShiftAssignment a,
        IReadOnlyDictionary<(int AssignmentId, int ScheduledBreakId), (TimeOnly Start, TimeOnly End)> breakWindows) =>
        a.Shift!.ScheduledBreaks
            .OrderBy(b => b.StartTime)
            .Select(b =>
            {
                var (start, end) = breakWindows.TryGetValue((a.Id, b.Id), out var window)
                    ? window
                    : (b.StartTime, b.EndTime);
                return new ScheduledBreakDto(b.Kind, start, end);
            })
            .ToList();
}
