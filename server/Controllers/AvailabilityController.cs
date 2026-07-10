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

        // Employees submit availability for exactly one week ahead ("next
        // week"), during the week before it, with a hard Saturday
        // deadline. Submitting doesn't lock anything by itself — it's just
        // a status marker for the admin roster — so the employee can keep
        // adjusting and re-submitting right up to the deadline. Once that
        // week isn't the open one anymore (wrong week or deadline passed),
        // every day is frozen: whatever was already on file (or blank) is
        // kept no matter what the client sends.
        var isLocked = !IsOpenWeek(request.WeekStartDate);

        ApplyDays(availability ??= NewAvailability(accountId, request.WeekStartDate), request.Days, isLocked);
        if (availability.Id == 0)
        {
            db.Availabilities.Add(availability);
        }

        if (request.Submit && !isLocked)
        {
            availability.IsSubmitted = true;
            availability.SubmittedAt = DateTime.UtcNow;
        }

        db.SaveChanges();

        return Ok(ToDto(availability, account, request.WeekStartDate));
    }

    // Admin/Sa override: unlike SaveMine, this bypasses the open-week/
    // deadline/submitted lock entirely, since the whole point is letting
    // an admin correct or fill in availability the employee can no longer
    // touch themselves.
    [HttpPut("{accountId:int}")]
    [Authorize(Policy = "AdminOrAbove")]
    public ActionResult<AvailabilityDto> SaveForAccount(int accountId, SaveAvailabilityRequest request)
    {
        var account = db.Accounts.Include(a => a.Location).SingleOrDefault(a => a.Id == accountId);
        if (account is null || !CanAccess(account.Location?.LocationCode))
        {
            return NotFound();
        }

        var availability = db.Availabilities
            .Include(a => a.Days)
            .SingleOrDefault(a => a.AccountId == accountId && a.WeekStartDate == request.WeekStartDate);

        ApplyDays(availability ??= NewAvailability(accountId, request.WeekStartDate), request.Days, isLocked: false);
        if (availability.Id == 0)
        {
            db.Availabilities.Add(availability);
        }

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
    // Admin and Lead accounts can work shifts too, so they're schedulable
    // alongside Employee accounts; only Sa (no location, not a worker) is excluded.
    [HttpGet]
    [Authorize(Policy = "AdminOrAbove")]
    public ActionResult<IEnumerable<AvailabilityDto>> GetForLocation(
        [FromQuery] string? locationCode, [FromQuery] DateOnly weekStartDate)
    {
        var accountsQuery = db.Accounts
            .Include(a => a.Location)
            .Where(a => a.Role == AccountRole.Employee || a.Role == AccountRole.Lead || a.Role == AccountRole.Admin);

        if (User.IsInRole(nameof(AccountRole.Sa)))
        {
            if (!string.IsNullOrWhiteSpace(locationCode))
            {
                accountsQuery = accountsQuery.Where(a => a.Location != null && a.Location.LocationCode == locationCode);
            }
        }
        else
        {
            var callerLocationCode = CallerLocationCode();
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

    private static Availability NewAvailability(int accountId, DateOnly weekStartDate) =>
        new() { AccountId = accountId, WeekStartDate = weekStartDate };

    private void ApplyDays(Availability availability, IEnumerable<AvailabilityDayDto> requestDays, bool isLocked)
    {
        var existingDaysByDate = availability.Days.ToDictionary(d => d.Date);
        if (availability.Days.Count > 0)
        {
            db.AvailabilityDays.RemoveRange(availability.Days);
        }

        availability.Days = requestDays
            .Select(d =>
            {
                if (isLocked && existingDaysByDate.TryGetValue(d.Date, out var existing))
                {
                    return new AvailabilityDay
                    {
                        Date = existing.Date,
                        IsAvailable = existing.IsAvailable,
                        StartTime = existing.StartTime,
                        EndTime = existing.EndTime,
                    };
                }

                var isAvailable = !isLocked && d.IsAvailable;
                return new AvailabilityDay
                {
                    Date = d.Date,
                    IsAvailable = isAvailable,
                    StartTime = isAvailable ? d.StartTime : null,
                    EndTime = isAvailable ? d.EndTime : null,
                };
            })
            .ToList();
    }

    // The only week self-service edits are ever allowed for: next week,
    // and only while today is still on or before this week's Saturday.
    private static bool IsOpenWeek(DateOnly weekStartDate)
    {
        var todayMonday = MondayOf(DateOnly.FromDateTime(DateTime.Now));
        var nextWeekMonday = todayMonday.AddDays(7);
        var deadline = todayMonday.AddDays(5); // Saturday of the current week

        return weekStartDate == nextWeekMonday && DateOnly.FromDateTime(DateTime.Now) <= deadline;
    }

    private static DateOnly MondayOf(DateOnly date)
    {
        var diff = date.DayOfWeek == DayOfWeek.Sunday ? -6 : DayOfWeek.Monday - date.DayOfWeek;
        return date.AddDays(diff);
    }

    private bool CanAccess(string? locationCode) =>
        User.IsInRole(nameof(AccountRole.Sa)) || (locationCode is not null && locationCode == CallerLocationCode());

    private string? CallerLocationCode() =>
        User.FindFirst(TokenService.LocationCodeClaimType)?.Value;

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
