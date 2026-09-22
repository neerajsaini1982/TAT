using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Server.Data;
using Server.Dtos;
using Server.Models;

namespace Server.Services;

public record PunchResult(int StatusCode, TimeEntryDto? Entry, string? Error);

// Shared clock-in/segment/clock-out state machine, driven by an explicit
// accountId instead of always trusting the caller's own JWT — lets both
// self-service punches (TimeEntriesController, accountId = the caller) and
// kiosk punches (KioskController, accountId = PIN-verified) share identical
// rules without duplicating them. bypassDeviceCheck skips the
// LocationSettings.ClockInAnywhere/AllowedPunchDevice restriction — a kiosk
// device IS the approved device by definition, same reasoning
// AdminClockOut/AdminEditTimes already apply to an admin's own override.
public class TimeEntryPunchService(AppDbContext db)
{
    public PunchResult ClockIn(int accountId, int shiftAssignmentId, string? clientIp, bool bypassDeviceCheck = false)
    {
        var assignment = db.ShiftAssignments
            .Include(a => a.Shift)
            .SingleOrDefault(a => a.Id == shiftAssignmentId);

        if (assignment is null || assignment.AccountId != accountId || !assignment.IsPublished)
        {
            return new PunchResult(StatusCodes.Status404NotFound, null, null);
        }

        var today = DateOnly.FromDateTime(DateTime.Now);
        if (assignment.Date != today)
        {
            return new PunchResult(StatusCodes.Status400BadRequest, null, "You can only clock in for today's shift.");
        }

        if (db.TimeEntries.Any(t => t.ShiftAssignmentId == assignment.Id))
        {
            return new PunchResult(StatusCodes.Status409Conflict, null, "Already clocked in for this shift.");
        }

        if (!bypassDeviceCheck)
        {
            var deviceError = CheckDeviceAllowed(assignment.Shift!.LocationId, clientIp);
            if (deviceError is not null)
            {
                return new PunchResult(StatusCodes.Status403Forbidden, null, deviceError);
            }
        }

        var windowMinutes = db.LocationSettings
            .Where(s => s.LocationId == assignment.Shift!.LocationId)
            .Select(s => (int?)s.ClockInWindowMinutes)
            .SingleOrDefault() ?? 15;

        var scheduledStart = assignment.Date.ToDateTime(assignment.Shift!.StartTime);
        var earliestAllowed = scheduledStart.AddMinutes(-windowMinutes);
        if (DateTime.Now < earliestAllowed)
        {
            return new PunchResult(StatusCodes.Status400BadRequest, null, $"You can't clock in until {earliestAllowed:h:mm tt}.");
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

        return new PunchResult(StatusCodes.Status200OK, ToDto(entry), null);
    }

    // Starts a new break/lunch segment — any number allowed per shift, but
    // only one open (no EndAt) at a time, enforced below.
    public PunchResult StartSegment(int accountId, int entryId, BreakKind kind, string? clientIp, bool bypassDeviceCheck = false) =>
        Transition(accountId, entryId, clientIp, bypassDeviceCheck, entry =>
        {
            if (entry.ClockOutAt is not null)
            {
                return "Already clocked out.";
            }
            if (entry.Segments.Any(s => s.EndAt is null))
            {
                return "End your current break or lunch before starting another.";
            }

            entry.Segments.Add(new TimeEntrySegment { Kind = kind, StartAt = DateTime.UtcNow });
            return null;
        });

    public PunchResult EndSegment(int accountId, int entryId, string? clientIp, bool bypassDeviceCheck = false) =>
        Transition(accountId, entryId, clientIp, bypassDeviceCheck, entry =>
        {
            var open = entry.Segments.FirstOrDefault(s => s.EndAt is null);
            if (open is null)
            {
                return "Not currently on a break or lunch.";
            }

            open.EndAt = DateTime.UtcNow;
            return null;
        });

    public PunchResult ClockOut(int accountId, int entryId, string? clientIp, bool bypassDeviceCheck = false) =>
        Transition(accountId, entryId, clientIp, bypassDeviceCheck, entry =>
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

    // Applies a state-transition function to the given account's own entry,
    // saving and returning the updated DTO on success, or the returned
    // message as a 400 when the transition isn't valid from the entry's
    // current state.
    private PunchResult Transition(int accountId, int entryId, string? clientIp, bool bypassDeviceCheck, Func<TimeEntry, string?> apply)
    {
        var entry = db.TimeEntries
            .Include(t => t.Segments)
            .Include(t => t.ShiftAssignment).ThenInclude(a => a!.Shift)
            .SingleOrDefault(t => t.Id == entryId);
        if (entry is null || entry.AccountId != accountId)
        {
            return new PunchResult(StatusCodes.Status404NotFound, null, null);
        }

        if (!bypassDeviceCheck)
        {
            var deviceError = CheckDeviceAllowed(entry.ShiftAssignment!.Shift!.LocationId, clientIp);
            if (deviceError is not null)
            {
                return new PunchResult(StatusCodes.Status403Forbidden, null, deviceError);
            }
        }

        var error = apply(entry);
        if (error is not null)
        {
            return new PunchResult(StatusCodes.Status400BadRequest, null, error);
        }

        db.SaveChanges();
        return new PunchResult(StatusCodes.Status200OK, ToDto(entry), null);
    }

    // When LocationSettings.ClockInAnywhere is off, restricts self-service
    // punches to IPs an Admin has approved for that location.
    // bypassDeviceCheck (kiosk, admin overrides) skips this entirely.
    private string? CheckDeviceAllowed(int locationId, string? clientIp)
    {
        var settings = db.LocationSettings.SingleOrDefault(s => s.LocationId == locationId);
        if (settings is null || settings.ClockInAnywhere)
        {
            return null;
        }

        var allowed = clientIp is not null && db.AllowedPunchDevices.Any(d => d.LocationId == locationId && d.IpAddress == clientIp);
        return allowed ? null : "Clock-in/out is restricted to approved devices at this location. Contact your admin.";
    }

    public static TimeEntryDto ToDto(TimeEntry t) => new(
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
        t.LeftEarly,
        t.LeftEarlyNote,
        t.LeftEarlyMarkedByAccountId,
        t.LeftEarlyMarkedAt,
        t.EditedByAccountId,
        t.EditedAt);
}
