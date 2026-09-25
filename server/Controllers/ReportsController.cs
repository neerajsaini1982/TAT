using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Server.Data;
using Server.Dtos;
using Server.Models;
using Server.Security;
using Server.Services;

namespace Server.Controllers;

// Reporting endpoints. Currently just the hours report (issue #18) — a
// nested by-employee/by-day breakdown of worked, break, and lunch time over
// a chosen date range, including absences. Any authenticated role can call
// GetHoursReport, but only Sa/Admin get every employee's rows — a plain
// Employee (or Lead) calling it is silently scoped down to just their own,
// so the same endpoint backs both the admin-facing Payroll Report page and
// an employee self-service report with no separate route needed.
[ApiController]
[Route("api/reports")]
[Authorize]
public class ReportsController(AppDbContext db, IEmailSender emailSender) : ControllerBase
{
    // Top level: one row per employee with totals across the range. Days is
    // the drill-down — one row per date the employee had a published shift
    // assignment, whether worked, absent, or still open. Employees with no
    // assignment in range don't appear at all (nothing to report), and a
    // draft (not-yet-posted) assignment doesn't count as "scheduled" either
    // — same rule as what employees themselves see (GetMine).
    //
    // Overtime comes from the location's rules (OvertimeCalculator). Weekly
    // and seventh-day rules span whole workweeks, so hours are loaded for
    // every workweek the range touches and then trimmed back to the range —
    // a report starting on a Wednesday still counts that week's Mon/Tue.
    [HttpGet("hours")]
    public ActionResult<IEnumerable<EmployeeHoursReportDto>> GetHoursReport(
        [FromQuery] string? locationCode, [FromQuery] DateOnly startDate, [FromQuery] DateOnly endDate)
    {
        if (endDate < startDate)
        {
            return BadRequest("endDate can't be before startDate.");
        }

        var location = ResolveLocation(locationCode);
        if (location is null)
        {
            return BadRequest("A valid locationCode is required.");
        }

        // Sa/Admin see the whole location; anyone else (Lead, Employee) only
        // ever gets their own row back, regardless of what locationCode was
        // requested — this is what makes GetHoursReport safe to expose to
        // every role instead of gating it behind AdminOrAbove.
        var canSeeEveryone = User.IsInRole(nameof(AccountRole.Sa)) || User.IsInRole(nameof(AccountRole.Admin));
        return Ok(BuildHoursReport(db, location, startDate, endDate, canSeeEveryone ? null : CallerAccountId()));
    }

    // Emails each employee in the report their own hours for the range, using
    // the location's PayrollHours template — one email per employee, and only
    // to those with an email on file. EmployeeIds narrows it to the rows the
    // admin is looking at (the page's employee filter); null means everyone.
    // TestToAddress sends just the first matching employee's email to that
    // address instead (subject prefixed [TEST]) so the admin can check the
    // real data before it goes out. Sending is best-effort per employee, like
    // ShiftAssignmentsController.SendScheduleEmails, but unlike there the
    // admin gets back exactly who was sent, skipped, and failed.
    [HttpPost("hours/email")]
    [Authorize(Policy = "AdminOrAbove")]
    public async Task<ActionResult<EmailHoursReportResultDto>> EmailHoursReport(EmailHoursReportRequest request)
    {
        if (request.EndDate < request.StartDate)
        {
            return BadRequest("endDate can't be before startDate.");
        }

        var location = ResolveLocation(request.LocationCode);
        if (location is null)
        {
            return BadRequest("A valid locationCode is required.");
        }

        var settings = db.LocationSettings.SingleOrDefault(s => s.LocationId == location.Id);
        if (settings is null || string.IsNullOrWhiteSpace(settings.SmtpHost))
        {
            return BadRequest("SMTP is not configured for this location. Set it up under Settings first.");
        }

        var report = BuildHoursReport(db, location, request.StartDate, request.EndDate, onlyAccountId: null);
        if (request.EmployeeIds is not null)
        {
            report = report.Where(r => request.EmployeeIds.Contains(r.EmployeeId)).ToList();
        }

        if (report.Count == 0)
        {
            return BadRequest("No employees have hours in this date range.");
        }

        var template = db.EmailTemplates.SingleOrDefault(
            t => t.LocationId == location.Id && t.Key == EmailTemplateKeys.PayrollHours)
            ?? EmailTemplateCatalog.Default(EmailTemplateKeys.PayrollHours);

        (string Subject, string Body) Render(EmployeeHoursReportDto row)
        {
            var placeholders = PayrollHoursEmail.Placeholders(row, location.Name, request.StartDate, request.EndDate, settings.DateFormat);
            return (EmailTemplateCatalog.Render(template.Subject, placeholders), EmailTemplateCatalog.Render(template.BodyHtml, placeholders));
        }

        if (!string.IsNullOrWhiteSpace(request.TestToAddress))
        {
            var sample = report[0];
            var (subject, body) = Render(sample);
            try
            {
                await emailSender.SendAsync(settings, request.TestToAddress, $"[TEST] {subject}", body);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (Exception)
            {
                return StatusCode(StatusCodes.Status502BadGateway, "Failed to send email. Check the SMTP settings and try again.");
            }

            return Ok(new EmailHoursReportResultDto([sample.FullName], [], []));
        }

        var employeeIds = report.Select(r => r.EmployeeId).ToList();
        var emailsById = db.Accounts
            .Where(a => employeeIds.Contains(a.Id))
            .ToDictionary(a => a.Id, a => a.Email);

        var sent = new List<string>();
        var skippedNoEmail = new List<string>();
        var failed = new List<string>();
        foreach (var row in report)
        {
            var email = emailsById.GetValueOrDefault(row.EmployeeId);
            if (string.IsNullOrWhiteSpace(email))
            {
                skippedNoEmail.Add(row.FullName);
                continue;
            }

            var (subject, body) = Render(row);
            try
            {
                await emailSender.SendAsync(settings, email, subject, body);
                sent.Add(row.FullName);
            }
            catch
            {
                // Best-effort — one bad address shouldn't stop the rest.
                failed.Add(row.FullName);
            }
        }

        return Ok(new EmailHoursReportResultDto(sent, skippedNoEmail, failed));
    }

    // The whole report for a location, or just onlyAccountId's row. Shared
    // with EmailHoursReport and EmailTemplatesController.SendTest so an
    // emailed report can never disagree with what the page shows.
    internal static List<EmployeeHoursReportDto> BuildHoursReport(
        AppDbContext db, Location location, DateOnly startDate, DateOnly endDate, int? onlyAccountId)
    {
        var settings = db.LocationSettings.SingleOrDefault(s => s.LocationId == location.Id);
        var breakLimitMinutes = settings?.BreakLimitMinutes ?? 15;
        var lunchLimitMinutes = settings?.LunchLimitMinutes ?? 30;
        // No settings row yet means the defaults (daily overtime after 8 hours).
        var overtimePolicy = (settings ?? new LocationSettings()).GetOvertimePolicy();
        var (loadStart, loadEnd) = OvertimeCalculator.WorkweekSpan(startDate, endDate, overtimePolicy.WorkweekStartDay);

        var assignmentsQuery = db.ShiftAssignments
            .Include(a => a.Account)
            .Include(a => a.Shift).ThenInclude(s => s!.ScheduledBreaks)
            .Where(a => a.Shift!.LocationId == location.Id && a.Date >= loadStart && a.Date <= loadEnd && a.IsPublished);
        if (onlyAccountId is not null)
        {
            assignmentsQuery = assignmentsQuery.Where(a => a.AccountId == onlyAccountId);
        }

        var assignments = assignmentsQuery.ToList();

        // Sick time recorded for a day with no ShiftAssignment at all (see
        // SickTimeEntriesController) — folded into the same report so an
        // employee out sick on an unscheduled day still shows up.
        var sickEntriesQuery = db.SickTimeEntries
            .Include(s => s.Account)
            .Where(s => s.Account!.LocationId == location.Id && s.Date >= startDate && s.Date <= endDate);
        if (onlyAccountId is not null)
        {
            sickEntriesQuery = sickEntriesQuery.Where(s => s.AccountId == onlyAccountId);
        }

        var sickEntries = sickEntriesQuery.ToList();

        var assignmentIds = assignments.Select(a => a.Id).ToList();
        var entriesByAssignmentId = db.TimeEntries
            .Include(t => t.Segments)
            .Where(t => assignmentIds.Contains(t.ShiftAssignmentId))
            .ToDictionary(t => t.ShiftAssignmentId);

        var accountsById = assignments.Select(a => a.Account!)
            .Concat(sickEntries.Select(s => s.Account!))
            .GroupBy(a => a.Id)
            .ToDictionary(g => g.Key, g => g.First());
        var assignmentsByAccountId = assignments.ToLookup(a => a.AccountId);
        var sickEntriesByAccountId = sickEntries.ToLookup(s => s.AccountId);

        // Drop employees with nothing to show for the range (no net worked
        // time, e.g. scheduled but never clocked in, or absent) — but keep
        // them if they currently have an open entry (clocked in, whether or
        // not any time has accrued yet, or clocked in and not yet clocked
        // out), or have sick hours recorded, since those are still worth an
        // admin's attention/payroll entry even at 0 worked minutes.
        return accountsById.Keys
            .Select(accountId => BuildEmployeeReport(
                accountsById[accountId],
                assignmentsByAccountId[accountId].ToList(),
                sickEntriesByAccountId[accountId].ToList(),
                entriesByAssignmentId,
                breakLimitMinutes,
                lunchLimitMinutes,
                overtimePolicy,
                startDate,
                endDate))
            .Where(e => e.TotalNetWorkedMinutes > 0 || e.OpenEntryDays > 0 || e.TotalSickMinutes > 0)
            .OrderBy(e => e.FullName)
            .ToList();
    }

    private static EmployeeHoursReportDto BuildEmployeeReport(
        Account account,
        List<ShiftAssignment> assignments,
        List<SickTimeEntry> sickEntries,
        Dictionary<int, TimeEntry> entriesByAssignmentId,
        int breakLimitMinutes,
        int lunchLimitMinutes,
        OvertimePolicy overtimePolicy,
        DateOnly startDate,
        DateOnly endDate)
    {
        // Union of every date with either a shift assignment or a manually
        // recorded sick entry — a date can have one, the other, or both.
        var dates = assignments.Select(a => a.Date)
            .Concat(sickEntries.Select(s => s.Date))
            .Distinct()
            .OrderBy(d => d);

        var assignmentsByDate = assignments.ToLookup(a => a.Date);
        var sickEntriesByDate = sickEntries.ToLookup(s => s.Date);

        var days = dates
            .Select(date => BuildDay(
                date,
                assignmentsByDate[date].ToList(),
                sickEntriesByDate[date].ToList(),
                entriesByAssignmentId,
                breakLimitMinutes,
                lunchLimitMinutes))
            .ToList();

        // Rules run over every loaded day (whole workweeks), then only the
        // requested range is kept — see GetHoursReport. An exempt employee is
        // owed no premium pay, so no rules apply to them.
        var payByDate = OvertimeCalculator
            .Calculate(account.IsOvertimeExempt ? OvertimePolicy.None : overtimePolicy, days
                .Where(d => d.NetWorkedMinutes is not null)
                .Select(d => new DayHours(d.Date, d.NetWorkedMinutes!.Value)))
            .ToDictionary(p => p.Date);

        days = days
            .Where(d => d.Date >= startDate && d.Date <= endDate)
            .Select(d => payByDate.TryGetValue(d.Date, out var pay)
                ? d with
                {
                    RegularMinutes = pay.RegularMinutes,
                    OvertimeMinutes = pay.OvertimeMinutes,
                    DoubleTimeMinutes = pay.DoubleTimeMinutes,
                }
                : d)
            .ToList();

        return new EmployeeHoursReportDto(
            account.Id,
            $"{account.FirstName} {account.LastName}",
            account.IsOvertimeExempt,
            days.Sum(d => d.WorkedMinutes ?? 0),
            days.Sum(d => d.BreakMinutes),
            days.Sum(d => d.LunchMinutes),
            days.Sum(d => d.NetWorkedMinutes ?? 0),
            days.Sum(d => d.ScheduledMinutes ?? 0),
            days.Sum(d => d.RegularMinutes),
            days.Sum(d => d.OvertimeMinutes),
            days.Sum(d => d.DoubleTimeMinutes),
            days.Count(d => d.IsAbsent),
            days.Count(d => d.StillClockedIn),
            days.Sum(d => d.SickMinutes),
            days);
    }

    // Usually one assignment per employee per date, but folds in more than
    // one just in case (e.g. a split shift) by summing their entries.
    // dayAssignments can be empty — a date with only a manually recorded
    // SickTimeEntry and no assignment at all.
    private static DailyHoursDto BuildDay(
        DateOnly date,
        List<ShiftAssignment> dayAssignments,
        List<SickTimeEntry> daySickEntries,
        Dictionary<int, TimeEntry> entriesByAssignmentId,
        int breakLimitMinutes,
        int lunchLimitMinutes)
    {
        var isAbsent = dayAssignments.Any(a => a.IsAbsent);
        var absenceNote = dayAssignments.FirstOrDefault(a => a.IsAbsent)?.AbsenceNote;

        int? workedMinutes = null;
        var breakMinutes = 0;
        var lunchMinutes = 0;
        var hasLongBreak = false;
        var hasLongLunch = false;
        var stillClockedIn = false;
        var notes = new List<string>();
        var leftEarly = false;
        string? leftEarlyNote = null;

        foreach (var assignment in dayAssignments)
        {
            if (!entriesByAssignmentId.TryGetValue(assignment.Id, out var entry))
            {
                continue;
            }

            if (entry.ClockOutAt is null)
            {
                stillClockedIn = true;
            }
            else
            {
                workedMinutes = (workedMinutes ?? 0) + (int)(entry.ClockOutAt.Value - entry.ClockInAt).TotalMinutes;
            }

            foreach (var segment in entry.Segments)
            {
                if (segment.EndAt is null)
                {
                    continue; // still on this break/lunch — not yet counted
                }

                var minutes = (int)(segment.EndAt.Value - segment.StartAt).TotalMinutes;
                if (segment.Kind == BreakKind.Break)
                {
                    breakMinutes += minutes;
                    hasLongBreak = hasLongBreak || minutes > breakLimitMinutes;
                }
                else
                {
                    lunchMinutes += minutes;
                    hasLongLunch = hasLongLunch || minutes > lunchLimitMinutes;
                }
            }

            if (!string.IsNullOrWhiteSpace(entry.Note))
            {
                notes.Add(entry.Note);
            }

            if (entry.LeftEarly)
            {
                leftEarly = true;
                leftEarlyNote = entry.LeftEarlyNote;
            }
        }

        // Per the report spec: net worked time is worked time less lunch
        // only — break time is not subtracted out.
        var netWorkedMinutes = workedMinutes is not null ? workedMinutes - lunchMinutes : null;

        // Split shifts (rare) fold multiple assignments into one day row;
        // sick minutes are summed across them, but an admin edit needs one
        // concrete assignment to target — the first, same tiebreak as
        // AbsenceNote above. No assignment at all (sick entered manually on
        // an unscheduled day) leaves shiftAssignmentId null — nothing for
        // the editable sick-hours field to target.
        int? scheduledMinutes = dayAssignments.Count > 0
            ? dayAssignments.Sum(a => ScheduledMinutesFor(a.Shift!))
            : null;

        var sickMinutes = dayAssignments.Sum(a => a.SickMinutes) + daySickEntries.Sum(s => s.Minutes);
        var shiftAssignmentId = dayAssignments.Count > 0 ? dayAssignments[0].Id : (int?)null;
        var manualSickNotes = daySickEntries
            .Where(s => !string.IsNullOrWhiteSpace(s.Note))
            .Select(s => s.Note!)
            .ToList();

        return new DailyHoursDto(
            date, workedMinutes, breakMinutes, lunchMinutes, netWorkedMinutes, scheduledMinutes,
            // Regular/overtime/double-time are filled in by BuildEmployeeReport,
            // which needs the whole workweek to split them.
            RegularMinutes: 0, OvertimeMinutes: 0, DoubleTimeMinutes: 0,
            isAbsent, absenceNote, leftEarly, leftEarlyNote, stillClockedIn, hasLongBreak, hasLongLunch, notes,
            sickMinutes, shiftAssignmentId, manualSickNotes);
    }

    // Shift span less its scheduled lunch. A window whose end isn't after its
    // start runs past midnight, so a day is added.
    private static int ScheduledMinutesFor(Shift shift)
    {
        var lunchMinutes = shift.ScheduledBreaks
            .Where(b => b.Kind == BreakKind.Lunch)
            .Sum(b => SpanMinutes(b.StartTime, b.EndTime));
        return Math.Max(0, SpanMinutes(shift.StartTime, shift.EndTime) - lunchMinutes);
    }

    private static int SpanMinutes(TimeOnly start, TimeOnly end)
    {
        var minutes = (int)(end - start).TotalMinutes;
        return minutes > 0 ? minutes : minutes + 24 * 60;
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

    private string? CallerLocationCode() =>
        User.FindFirst(TokenService.LocationCodeClaimType)?.Value;

    private int CallerAccountId() =>
        int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
