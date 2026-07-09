using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Server.Data;
using Server.Dtos;
using Server.Models;
using Server.Security;

namespace Server.Controllers;

[ApiController]
[Route("api/availability")]
[Authorize]
public class AvailabilityController(AppDbContext db) : ControllerBase
{
    // Every authenticated account (Employee, Lead, Admin, Sa) manages only
    // its own availability through these two endpoints.
    [HttpGet("mine")]
    public ActionResult<AvailabilityDto> GetMine([FromQuery] DateOnly weekStartDate)
    {
        var accountId = CallerAccountId();
        var account = db.Accounts.Find(accountId)!;

        var availability = db.Availabilities
            .Include(a => a.Days)
            .SingleOrDefault(a => a.AccountId == accountId && a.WeekStartDate == weekStartDate);

        return Ok(ToDto(availability, account, weekStartDate));
    }

    [HttpPut("mine")]
    public ActionResult<AvailabilityDto> SaveMine(SaveAvailabilityRequest request)
    {
        var accountId = CallerAccountId();
        var account = db.Accounts.Find(accountId)!;

        var availability = db.Availabilities
            .Include(a => a.Days)
            .SingleOrDefault(a => a.AccountId == accountId && a.WeekStartDate == request.WeekStartDate);

        if (availability is not null && availability.IsSubmitted)
        {
            return Conflict("Availability for this week has already been submitted and cannot be changed.");
        }

        if (availability is null)
        {
            availability = new Availability { AccountId = accountId, WeekStartDate = request.WeekStartDate };
            db.Availabilities.Add(availability);
        }
        else
        {
            db.AvailabilityDays.RemoveRange(availability.Days);
        }

        availability.Days = request.Days
            .Select(d => new AvailabilityDay
            {
                Date = d.Date,
                IsAvailable = d.IsAvailable,
                StartTime = d.IsAvailable ? d.StartTime : null,
                EndTime = d.IsAvailable ? d.EndTime : null,
            })
            .ToList();

        if (request.Submit)
        {
            availability.IsSubmitted = true;
            availability.SubmittedAt = DateTime.UtcNow;
        }

        db.SaveChanges();

        return Ok(ToDto(availability, account, request.WeekStartDate));
    }

    // Read-only roster for a location/week so admins can see who has
    // submitted and who hasn't, replacing the old WhatsApp thread.
    [HttpGet]
    [Authorize(Policy = "AdminOrAbove")]
    public ActionResult<IEnumerable<AvailabilityDto>> GetForLocation(
        [FromQuery] string? locationCode, [FromQuery] DateOnly weekStartDate)
    {
        var accountsQuery = db.Accounts
            .Include(a => a.Location)
            .Where(a => a.Role == AccountRole.Employee || a.Role == AccountRole.Lead);

        if (User.IsInRole(nameof(AccountRole.Sa)))
        {
            if (!string.IsNullOrWhiteSpace(locationCode))
            {
                accountsQuery = accountsQuery.Where(a => a.Location != null && a.Location.LocationCode == locationCode);
            }
        }
        else
        {
            var callerLocationCode = User.FindFirst(TokenService.LocationCodeClaimType)?.Value;
            accountsQuery = accountsQuery.Where(a => a.Location != null && a.Location.LocationCode == callerLocationCode);
        }

        var accounts = accountsQuery.OrderBy(a => a.Username).ToList();
        var accountIds = accounts.Select(a => a.Id).ToList();

        var availabilities = db.Availabilities
            .Include(a => a.Days)
            .Where(a => accountIds.Contains(a.AccountId) && a.WeekStartDate == weekStartDate)
            .ToList()
            .ToDictionary(a => a.AccountId);

        var result = accounts.Select(account =>
            ToDto(availabilities.GetValueOrDefault(account.Id), account, weekStartDate));

        return Ok(result);
    }

    private int CallerAccountId() =>
        int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private static AvailabilityDto ToDto(Availability? availability, Account account, DateOnly weekStartDate)
    {
        if (availability is null)
        {
            var blankDays = Enumerable.Range(0, 7)
                .Select(offset => new AvailabilityDayDto(weekStartDate.AddDays(offset), false, null, null))
                .ToList();
            return new AvailabilityDto(0, account.Id, account.Username, account.FirstName, account.LastName, weekStartDate, false, null, blankDays);
        }

        var days = availability.Days
            .OrderBy(d => d.Date)
            .Select(d => new AvailabilityDayDto(d.Date, d.IsAvailable, d.StartTime, d.EndTime))
            .ToList();

        return new AvailabilityDto(
            availability.Id,
            account.Id,
            account.Username,
            account.FirstName,
            account.LastName,
            availability.WeekStartDate,
            availability.IsSubmitted,
            availability.SubmittedAt,
            days);
    }
}
