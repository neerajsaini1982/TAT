using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Server.Data;
using Server.Dtos;
using Server.Models;
using Server.Security;

namespace Server.Controllers;

// Admin-managed rehire history for one employee — Account.HireDate is the
// simple "current" value shown on the account form; this is the audit trail
// for employees who left and came back. The two are not auto-synced.
[ApiController]
[Route("api/accounts/{accountId:int}/employment-periods")]
[Authorize(Policy = "AdminOrAbove")]
public class EmploymentPeriodsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public ActionResult<IEnumerable<EmploymentPeriodDto>> GetAll(int accountId)
    {
        var account = db.Accounts.Include(a => a.Location).SingleOrDefault(a => a.Id == accountId);
        if (account is null || !CanAccess(account))
        {
            return NotFound();
        }

        var periods = db.EmploymentPeriods
            .Where(p => p.AccountId == accountId)
            .OrderByDescending(p => p.HireDate)
            .ToList();

        return Ok(periods.Select(ToDto));
    }

    [HttpPost]
    public ActionResult<EmploymentPeriodDto> Create(int accountId, CreateEmploymentPeriodRequest request)
    {
        var account = db.Accounts.Include(a => a.Location).SingleOrDefault(a => a.Id == accountId);
        if (account is null || !CanAccess(account))
        {
            return NotFound();
        }

        var period = new EmploymentPeriod
        {
            AccountId = accountId,
            HireDate = request.HireDate,
            EndDate = request.EndDate,
            Notes = request.Notes,
        };

        db.EmploymentPeriods.Add(period);
        db.SaveChanges();

        return CreatedAtAction(nameof(GetAll), new { accountId }, ToDto(period));
    }

    [HttpPut("{id:int}")]
    public ActionResult<EmploymentPeriodDto> Update(int accountId, int id, UpdateEmploymentPeriodRequest request)
    {
        var account = db.Accounts.Include(a => a.Location).SingleOrDefault(a => a.Id == accountId);
        if (account is null || !CanAccess(account))
        {
            return NotFound();
        }

        var period = db.EmploymentPeriods.SingleOrDefault(p => p.Id == id && p.AccountId == accountId);
        if (period is null)
        {
            return NotFound();
        }

        period.HireDate = request.HireDate;
        period.EndDate = request.EndDate;
        period.Notes = request.Notes;
        db.SaveChanges();

        return Ok(ToDto(period));
    }

    [HttpDelete("{id:int}")]
    public IActionResult Delete(int accountId, int id)
    {
        var account = db.Accounts.Include(a => a.Location).SingleOrDefault(a => a.Id == accountId);
        if (account is null || !CanAccess(account))
        {
            return NotFound();
        }

        var period = db.EmploymentPeriods.SingleOrDefault(p => p.Id == id && p.AccountId == accountId);
        if (period is null)
        {
            return NotFound();
        }

        db.EmploymentPeriods.Remove(period);
        db.SaveChanges();
        return NoContent();
    }

    private bool CanAccess(Account account) =>
        User.IsInRole(nameof(AccountRole.Sa)) ||
        (account.Location is not null && account.Location.LocationCode == CallerLocationCode());

    private string? CallerLocationCode() =>
        User.FindFirst(TokenService.LocationCodeClaimType)?.Value;

    private static EmploymentPeriodDto ToDto(EmploymentPeriod p) => new(
        p.Id,
        p.HireDate.ToString("yyyy-MM-dd"),
        p.EndDate?.ToString("yyyy-MM-dd"),
        p.Notes);
}
