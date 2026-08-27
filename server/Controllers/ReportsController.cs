using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Server.Data;
using Server.Dtos;
using Server.Models;
using Server.Security;

namespace Server.Controllers;

// Reporting endpoints. Currently just the hours report (issue #18) — a
// nested by-employee/by-day breakdown of worked, break, and lunch time over
// a chosen date range, including absences. Any authenticated role can call
// GetHoursReport, but only Sa/Admin get every employee's rows — a plain
// Employee (or Lead) calling it is silently scoped down to just their own,
// so the same admin reports page/endpoint doubles as an employee
// self-service report with no separate route needed.
[ApiController]
[Route("api/reports")]
[Authorize]
public class ReportsController(AppDbContext db) : ControllerBase
{
    // Top level: one row per employee with totals across the range. Days is
    // the drill-down — one row per date the employee had a published shift
    // assignment, whether worked, absent, or still open. Employees with no
    // assignment in range don't appear at all (nothing to report), and a
    // draft (not-yet-posted) assignment doesn't count as "scheduled" either
    // — same rule as what employees themselves see (GetMine).
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

        var settings = db.LocationSettings.SingleOrDefault(s => s.LocationId == location.Id);
        var breakLimitMinutes = settings?.BreakLimitMinutes ?? 15;
        var lunchLimitMinutes = settings?.LunchLimitMinutes ?? 30;
        var overtimeThresholdMinutes = settings?.OvertimeDailyThresholdMinutes ?? 480;

        // Sa/Admin see the whole location; anyone else (Lead, Employee) only
        // ever gets their own row back, regardless of what locationCode was
        // requested — this is what makes GetHoursReport safe to expose to
        // every role instead of gating it behind AdminOrAbove.
        var canSeeEveryone = User.IsInRole(nameof(AccountRole.Sa)) || User.IsInRole(nameof(AccountRole.Admin));
        var callerAccountId = CallerAccountId();

        var assignmentsQuery = db.ShiftAssignments
            .Include(a => a.Account)
            .Where(a => a.Shift!.LocationId == location.Id && a.Date >= startDate && a.Date <= endDate && a.IsPublished);
        if (!canSeeEveryone)
        {
            assignmentsQuery = assignmentsQuery.Where(a => a.AccountId == callerAccountId);
        }

        var assignments = assignmentsQuery.ToList();

        var assignmentIds = assignments.Select(a => a.Id).ToList();
        var entriesByAssignmentId = db.TimeEntries
            .Include(t => t.Segments)
            .Where(t => assignmentIds.Contains(t.ShiftAssignmentId))
            .ToDictionary(t => t.ShiftAssignmentId);

        // Drop employees with nothing to show for the range (no net worked
        // time, e.g. scheduled but never clocked in, or absent) — but keep
        // them if they currently have an open entry (clocked in, whether or
        // not any time has accrued yet, or clocked in and not yet clocked
        // out), or have sick hours recorded, since those are still worth an
        // admin's attention/payroll entry even at 0 worked minutes.
        var report = assignments
            .GroupBy(a => a.AccountId)
            .Select(g => BuildEmployeeReport(g.First().Account!, g.ToList(), entriesByAssignmentId, breakLimitMinutes, lunchLimitMinutes, overtimeThresholdMinutes))
            .Where(e => e.TotalNetWorkedMinutes > 0 || e.OpenEntryDays > 0 || e.TotalSickMinutes > 0)
            .OrderBy(e => e.FullName)
            .ToList();

        return Ok(report);
    }

    private static EmployeeHoursReportDto BuildEmployeeReport(
        Account account,
        List<ShiftAssignment> assignments,
        Dictionary<int, TimeEntry> entriesByAssignmentId,
        int breakLimitMinutes,
        int lunchLimitMinutes,
        int overtimeThresholdMinutes)
    {
        var days = assignments
            .GroupBy(a => a.Date)
            .OrderBy(g => g.Key)
            .Select(g => BuildDay(g.Key, g.ToList(), entriesByAssignmentId, breakLimitMinutes, lunchLimitMinutes, overtimeThresholdMinutes))
            .ToList();

        return new EmployeeHoursReportDto(
            account.Id,
            $"{account.FirstName} {account.LastName}",
            days.Sum(d => d.WorkedMinutes ?? 0),
            days.Sum(d => d.BreakMinutes),
            days.Sum(d => d.LunchMinutes),
            days.Sum(d => d.NetWorkedMinutes ?? 0),
            days.Sum(d => d.OvertimeMinutes),
            days.Count(d => d.IsAbsent),
            days.Count(d => d.StillClockedIn),
            days.Sum(d => d.SickMinutes),
            days);
    }

    // Usually one assignment per employee per date, but folds in more than
    // one just in case (e.g. a split shift) by summing their entries.
    private static DailyHoursDto BuildDay(
        DateOnly date,
        List<ShiftAssignment> dayAssignments,
        Dictionary<int, TimeEntry> entriesByAssignmentId,
        int breakLimitMinutes,
        int lunchLimitMinutes,
        int overtimeThresholdMinutes)
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
        var overtimeMinutes = netWorkedMinutes is not null ? Math.Max(0, netWorkedMinutes.Value - overtimeThresholdMinutes) : 0;

        // Split shifts (rare) fold multiple assignments into one day row;
        // sick minutes are summed across them, but an admin edit needs one
        // concrete assignment to target — the first, same tiebreak as
        // AbsenceNote above.
        var sickMinutes = dayAssignments.Sum(a => a.SickMinutes);
        var shiftAssignmentId = dayAssignments[0].Id;

        return new DailyHoursDto(
            date, workedMinutes, breakMinutes, lunchMinutes, netWorkedMinutes, overtimeMinutes,
            isAbsent, absenceNote, leftEarly, leftEarlyNote, stillClockedIn, hasLongBreak, hasLongLunch, notes,
            sickMinutes, shiftAssignmentId);
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
