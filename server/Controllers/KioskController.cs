using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Server.Data;
using Server.Dtos;
using Server.Security;
using Server.Services;

namespace Server.Controllers;

// Drives a shared kiosk device's roster + punches. The device itself is
// authenticated by a per-location kiosk passcode (see
// AuthController.KioskLogin/TokenService.CreateKioskToken) — every action
// here additionally verifies a PIN for the specific employee being punched
// (KioskPinService), since the device's own JWT never identifies one
// employee. Always scoped to the kiosk token's own locationCode claim, never
// a caller-supplied one, so one kiosk device can't punch for another
// location. Reuses TimeEntryPunchService for the actual punch rules (same
// ones self-service TimeEntriesController uses) and
// ShiftAssignmentsController's ToDto/ComputeBreakWindows for the roster
// shape, just with accountId supplied explicitly instead of read off the
// caller's own JWT.
[ApiController]
[Route("api/kiosk")]
[Authorize(Policy = "Kiosk")]
public class KioskController(AppDbContext db, KioskPinService pinService, TimeEntryPunchService punchService) : ControllerBase
{
    // Today's published shifts for this kiosk's location — kiosk is a
    // same-day device, not a week browser, so unlike
    // ShiftAssignmentsController.GetForWeek this always means today.
    [HttpGet("schedule")]
    public ActionResult<IEnumerable<ShiftAssignmentDto>> GetSchedule()
    {
        var locationCode = CallerLocationCode();
        var location = db.Locations.SingleOrDefault(l => l.LocationCode == locationCode);
        if (location is null)
        {
            return NotFound();
        }

        var today = DateOnly.FromDateTime(DateTime.Now);
        var assignments = db.ShiftAssignments
            .Include(a => a.Shift).ThenInclude(s => s!.ScheduledBreaks)
            .Include(a => a.Account)
            .Where(a => a.Shift!.LocationId == location.Id && a.Date == today && a.IsPublished)
            .OrderBy(a => a.Shift!.StartTime)
            .ThenBy(a => a.Account!.FirstName)
            .ThenBy(a => a.Account!.LastName)
            .ToList();

        var breakWindows = ShiftAssignmentsController.ComputeBreakWindows(db, assignments);
        return Ok(assignments.Select(a => ShiftAssignmentsController.ToDto(a, breakWindows)));
    }

    // Today's punches for this kiosk's location, so the roster can show who
    // is already clocked in / on break / clocked out.
    [HttpGet("time-entries")]
    public ActionResult<IEnumerable<TimeEntryDto>> GetTimeEntries()
    {
        var locationCode = CallerLocationCode();
        var location = db.Locations.SingleOrDefault(l => l.LocationCode == locationCode);
        if (location is null)
        {
            return NotFound();
        }

        var today = DateOnly.FromDateTime(DateTime.Now);
        var entries = db.TimeEntries
            .Include(t => t.Segments)
            .Where(t => t.ShiftAssignment!.Date == today && t.ShiftAssignment.Shift!.LocationId == location.Id)
            .ToList();

        return Ok(entries.Select(TimeEntryPunchService.ToDto));
    }

    [HttpPost("clock-in")]
    public ActionResult<TimeEntryDto> ClockIn(KioskClockInRequest request)
    {
        var verify = pinService.VerifyAndConsume(request.AccountId, CallerLocationCode()!, request.Pin);
        if (!verify.Ok)
        {
            return StatusCode(StatusCodes.Status403Forbidden, verify.Error);
        }

        return ToActionResult(punchService.ClockIn(verify.Account!.Id, request.ShiftAssignmentId, clientIp: null, bypassDeviceCheck: true));
    }

    [HttpPost("{entryId:int}/segments/start")]
    public ActionResult<TimeEntryDto> StartSegment(int entryId, KioskStartSegmentRequest request)
    {
        var verify = pinService.VerifyAndConsume(request.AccountId, CallerLocationCode()!, request.Pin);
        if (!verify.Ok)
        {
            return StatusCode(StatusCodes.Status403Forbidden, verify.Error);
        }

        return ToActionResult(punchService.StartSegment(verify.Account!.Id, entryId, request.Kind, clientIp: null, bypassDeviceCheck: true));
    }

    [HttpPost("{entryId:int}/segments/end")]
    public ActionResult<TimeEntryDto> EndSegment(int entryId, KioskPinRequest request)
    {
        var verify = pinService.VerifyAndConsume(request.AccountId, CallerLocationCode()!, request.Pin);
        if (!verify.Ok)
        {
            return StatusCode(StatusCodes.Status403Forbidden, verify.Error);
        }

        return ToActionResult(punchService.EndSegment(verify.Account!.Id, entryId, clientIp: null, bypassDeviceCheck: true));
    }

    [HttpPost("{entryId:int}/clock-out")]
    public ActionResult<TimeEntryDto> ClockOut(int entryId, KioskPinRequest request)
    {
        var verify = pinService.VerifyAndConsume(request.AccountId, CallerLocationCode()!, request.Pin);
        if (!verify.Ok)
        {
            return StatusCode(StatusCodes.Status403Forbidden, verify.Error);
        }

        return ToActionResult(punchService.ClockOut(verify.Account!.Id, entryId, clientIp: null, bypassDeviceCheck: true));
    }

    private ActionResult<TimeEntryDto> ToActionResult(PunchResult result) => result.StatusCode switch
    {
        StatusCodes.Status200OK => Ok(result.Entry),
        StatusCodes.Status409Conflict => Conflict(result.Error),
        StatusCodes.Status404NotFound => NotFound(),
        _ => BadRequest(result.Error),
    };

    private string? CallerLocationCode() =>
        User.FindFirst(TokenService.LocationCodeClaimType)?.Value;
}
