using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Server.Data;
using Server.Dtos;
using Server.Models;
using Server.Security;

namespace Server.Controllers;

// Admin-only reporting endpoints. Currently just the hours report (issue
// #18) — a nested by-employee/by-day breakdown of worked, break, and lunch
// time over an admin-chosen date range, including absences.
[ApiController]
[Route("api/reports")]
[Authorize(Policy = "AdminOrAbove")]
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

        var assignments = db.ShiftAssignments
            .Include(a => a.Account)
            .Where(a => a.Shift!.LocationId == location.Id && a.Date >= startDate && a.Date <= endDate && a.IsPublished)
            .ToList();

        var assignmentIds = assignments.Select(a => a.Id).ToList();
        var entriesByAssignmentId = db.TimeEntries
            .Include(t => t.Segments)
            .Where(t => assignmentIds.Contains(t.ShiftAssignmentId))
            .ToDictionary(t => t.ShiftAssignmentId);

        var report = assignments
            .GroupBy(a => a.AccountId)
            .Select(g => BuildEmployeeReport(g.First().Account!, g.ToList(), entriesByAssignmentId, breakLimitMinutes, lunchLimitMinutes))
            .OrderBy(e => e.FullName)
            .ToList();

        return Ok(report);
    }

    private static EmployeeHoursReportDto BuildEmployeeReport(
        Account account,
        List<ShiftAssignment> assignments,
        Dictionary<int, TimeEntry> entriesByAssignmentId,
        int breakLimitMinutes,
        int lunchLimitMinutes)
    {
        var days = assignments
            .GroupBy(a => a.Date)
            .OrderBy(g => g.Key)
            .Select(g => BuildDay(g.Key, g.ToList(), entriesByAssignmentId, breakLimitMinutes, lunchLimitMinutes))
            .ToList();

        return new EmployeeHoursReportDto(
            account.Id,
            $"{account.FirstName} {account.LastName}",
            days.Sum(d => d.WorkedMinutes ?? 0),
            days.Sum(d => d.BreakMinutes),
            days.Sum(d => d.LunchMinutes),
            days.Sum(d => d.NetWorkedMinutes ?? 0),
            days.Count(d => d.IsAbsent),
            days.Count(d => d.StillClockedIn),
            days);
    }

    // Usually one assignment per employee per date, but folds in more than
    // one just in case (e.g. a split shift) by summing their entries.
    private static DailyHoursDto BuildDay(
        DateOnly date,
        List<ShiftAssignment> dayAssignments,
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
        }

        // Per the report spec: net worked time is worked time less lunch
        // only — break time is not subtracted out.
        var netWorkedMinutes = workedMinutes is not null ? workedMinutes - lunchMinutes : null;

        return new DailyHoursDto(
            date, workedMinutes, breakMinutes, lunchMinutes, netWorkedMinutes,
            isAbsent, absenceNote, stillClockedIn, hasLongBreak, hasLongLunch, notes);
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
}
