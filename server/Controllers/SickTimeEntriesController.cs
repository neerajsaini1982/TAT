using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Server.Data;
using Server.Dtos;
using Server.Models;
using Server.Security;

namespace Server.Controllers;

// Lets an Admin/Sa record sick hours for an employee on a day that has no
// ShiftAssignment at all (see ShiftAssignmentsController.SetSickMinutes for
// the already-scheduled case). ReportsController folds these into the same
// per-day/per-employee sick totals as ShiftAssignment.SickMinutes.
[ApiController]
[Route("api/sick-time-entries")]
[Authorize]
public class SickTimeEntriesController(AppDbContext db) : ControllerBase
{
    [HttpPost]
    [Authorize(Policy = "AdminOrAbove")]
    public ActionResult<SickTimeEntryDto> Create(CreateSickTimeEntryRequest request)
    {
        if (request.Minutes <= 0)
        {
            return BadRequest("Sick hours must be greater than 0.");
        }

        var account = db.Accounts.Find(request.AccountId);
        if (account is null || !CanAccess(account))
        {
            return BadRequest("Employee must belong to a location you manage.");
        }

        var entry = new SickTimeEntry
        {
            AccountId = account.Id,
            Date = request.Date,
            Minutes = request.Minutes,
            Note = request.Note,
            RecordedByAccountId = CallerAccountId(),
            RecordedAt = DateTime.UtcNow,
        };

        db.SickTimeEntries.Add(entry);
        db.SaveChanges();

        entry.Account = account;
        return Ok(ToDto(entry));
    }

    private bool CanAccess(Account account)
    {
        if (User.IsInRole(nameof(AccountRole.Sa)))
        {
            return true;
        }

        var callerLocationCode = User.FindFirst(TokenService.LocationCodeClaimType)?.Value;
        var location = db.Locations.SingleOrDefault(l => l.LocationCode == callerLocationCode);
        return location is not null && account.LocationId == location.Id;
    }

    private int CallerAccountId() =>
        int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private static SickTimeEntryDto ToDto(SickTimeEntry e) => new(
        e.Id,
        e.AccountId,
        e.Account!.FirstName,
        e.Account.LastName,
        e.Date,
        e.Minutes,
        e.Note,
        e.RecordedByAccountId,
        e.RecordedAt);
}
