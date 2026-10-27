using Microsoft.EntityFrameworkCore;
using Server.Controllers;
using Server.Data;
using Server.Models;

namespace Server.Services;

// Shared time-off logic used by the time-off controllers, the scheduling
// checks in ShiftAssignmentsController and the hours report.
public static class TimeOffRules
{
    public static readonly string[] DefaultTypeNames =
        ["Vacation", "Personal", "Bereavement", "Jury Duty", "Unpaid", "Other"];

    // A location gets the default types (all unpaid) the first time anything
    // asks for its list — same "create on first access" approach as
    // LocationSettings, so new and existing locations both get them without
    // a data migration.
    public static void EnsureDefaultTypes(AppDbContext db, int locationId)
    {
        if (db.TimeOffTypes.Any(t => t.LocationId == locationId))
        {
            return;
        }

        db.TimeOffTypes.AddRange(DefaultTypeNames.Select((name, i) => new TimeOffType
        {
            LocationId = locationId,
            Name = name,
            IsPaid = false,
            IsActive = true,
            SortOrder = i,
        }));
        db.SaveChanges();
    }

    public static LocationSettings SettingsFor(AppDbContext db, int locationId) =>
        db.LocationSettings.SingleOrDefault(s => s.LocationId == locationId) ?? new LocationSettings { LocationId = locationId };

    public static DateOnly LocationToday(LocationSettings settings)
    {
        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(settings.TimeZone);
            return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone));
        }
        catch (TimeZoneNotFoundException)
        {
            return DateOnly.FromDateTime(DateTime.UtcNow);
        }
    }

    public static IEnumerable<DateOnly> EachDay(DateOnly start, DateOnly end)
    {
        for (var d = start; d <= end; d = d.AddDays(1))
        {
            yield return d;
        }
    }

    // Approved, full-day time off covering this date — what blocks putting
    // the employee on a shift.
    public static TimeOffRequest? ApprovedFullDayOff(AppDbContext db, int accountId, DateOnly date) =>
        db.TimeOffRequests
            .Include(r => r.TimeOffType)
            .FirstOrDefault(r => r.AccountId == accountId
                && r.Status == TimeOffStatus.Approved
                && !r.IsPartialDay
                && r.StartDate <= date && r.EndDate >= date);

    // Minutes each day of a request would count for: a partial day is its
    // own minutes; a full day is the length of the shift the employee has
    // that day, or the location's full-day length when they have none.
    public static List<TimeOffRequestDay> SuggestDays(AppDbContext db, TimeOffRequest request, LocationSettings settings)
    {
        if (request.IsPartialDay)
        {
            return [new TimeOffRequestDay { Date = request.StartDate, Minutes = request.PartialMinutes ?? 0 }];
        }

        var shiftMinutesByDate = db.ShiftAssignments
            .Include(a => a.Shift).ThenInclude(s => s!.ScheduledBreaks)
            .Where(a => a.AccountId == request.AccountId && a.Date >= request.StartDate && a.Date <= request.EndDate)
            .ToList()
            .GroupBy(a => a.Date)
            .ToDictionary(g => g.Key, g => g.Sum(a => ReportsController.ScheduledMinutesFor(a.Shift!)));

        return EachDay(request.StartDate, request.EndDate)
            .Select(date => new TimeOffRequestDay
            {
                Date = date,
                Minutes = shiftMinutesByDate.TryGetValue(date, out var minutes) ? minutes : settings.TimeOffFullDayMinutes,
            })
            .ToList();
    }

    // Applies an admin's per-day overrides on top of the suggestion. Only
    // dates inside the request are accepted; minutes are clamped to a day.
    public static string? ApplyDayOverrides(List<TimeOffRequestDay> days, IEnumerable<Dtos.TimeOffRequestDayDto>? overrides)
    {
        if (overrides is null)
        {
            return null;
        }

        foreach (var o in overrides)
        {
            var day = days.FirstOrDefault(d => d.Date == o.Date);
            if (day is null)
            {
                return $"{o.Date:MM/dd/yyyy} isn't part of this request.";
            }

            if (o.Minutes < 0 || o.Minutes > 24 * 60)
            {
                return "Each day's hours must be between 0 and 24.";
            }

            day.Minutes = o.Minutes;
        }

        return null;
    }

    public static string FormatMinutes(int minutes)
    {
        var hours = minutes / 60;
        var mins = minutes % 60;
        return mins == 0 ? $"{hours}h" : hours > 0 ? $"{hours}h {mins}m" : $"{mins}m";
    }
}
