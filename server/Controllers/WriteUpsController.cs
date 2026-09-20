using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Server.Data;
using Server.Dtos;
using Server.Models;
using Server.Security;

namespace Server.Controllers;

// Write-ups / warnings recorded against one employee. Any authenticated
// caller can read their OWN list (that's how an employee sees their own
// write-ups); only Admin/Sa — for an employee in their own location — can
// read anyone else's, or create, edit, or delete. Leads get no special
// access: a write-up is HR-sensitive, so it isn't something to hand out by
// default. Lookups that fail the access check return 404 rather than 403, so
// the API doesn't confirm that another employee's account exists.
[ApiController]
[Route("api/accounts/{accountId:int}/write-ups")]
[Authorize]
public class WriteUpsController(AppDbContext db) : ControllerBase
{
    public const int MaxDescriptionLength = 2000;

    [HttpGet]
    public ActionResult<IEnumerable<WriteUpDto>> GetAll(int accountId)
    {
        var account = FindAccount(accountId);
        if (account is null || !CanView(account))
        {
            return NotFound();
        }

        var writeUps = db.WriteUps
            .Include(w => w.CreatedByAccount)
            .Where(w => w.AccountId == accountId)
            .OrderByDescending(w => w.Date)
            .ThenByDescending(w => w.Id)
            .ToList();

        return Ok(writeUps.Select(ToDto));
    }

    [HttpPost]
    [Authorize(Policy = "AdminOrAbove")]
    public ActionResult<WriteUpDto> Create(int accountId, CreateWriteUpRequest request)
    {
        var account = FindAccount(accountId);
        if (account is null || !IsAdmin(account))
        {
            return NotFound();
        }

        var severity = request.Severity ?? WriteUpSeverity.Normal;
        var validationError = Validate(request.Date, request.Description, severity);
        if (validationError is not null)
        {
            return BadRequest(validationError);
        }

        var callerId = CallerAccountId();
        var writeUp = new WriteUp
        {
            AccountId = accountId,
            Date = request.Date,
            Description = request.Description.Trim(),
            Severity = severity,
            CreatedByAccountId = callerId,
            CreatedAt = DateTime.UtcNow,
        };

        db.WriteUps.Add(writeUp);
        db.SaveChanges();

        writeUp.CreatedByAccount = db.Accounts.Find(callerId);
        return CreatedAtAction(nameof(GetAll), new { accountId }, ToDto(writeUp));
    }

    [HttpPut("{writeUpId:int}")]
    [Authorize(Policy = "AdminOrAbove")]
    public ActionResult<WriteUpDto> Update(int accountId, int writeUpId, UpdateWriteUpRequest request)
    {
        var account = FindAccount(accountId);
        if (account is null || !IsAdmin(account))
        {
            return NotFound();
        }

        var writeUp = db.WriteUps
            .Include(w => w.CreatedByAccount)
            .SingleOrDefault(w => w.Id == writeUpId && w.AccountId == accountId);
        if (writeUp is null)
        {
            return NotFound();
        }

        var validationError = Validate(request.Date, request.Description, request.Severity);
        if (validationError is not null)
        {
            return BadRequest(validationError);
        }

        writeUp.Date = request.Date;
        writeUp.Description = request.Description.Trim();
        writeUp.Severity = request.Severity;
        db.SaveChanges();

        return Ok(ToDto(writeUp));
    }

    [HttpDelete("{writeUpId:int}")]
    [Authorize(Policy = "AdminOrAbove")]
    public IActionResult Delete(int accountId, int writeUpId)
    {
        var account = FindAccount(accountId);
        if (account is null || !IsAdmin(account))
        {
            return NotFound();
        }

        var writeUp = db.WriteUps.SingleOrDefault(w => w.Id == writeUpId && w.AccountId == accountId);
        if (writeUp is null)
        {
            return NotFound();
        }

        db.WriteUps.Remove(writeUp);
        db.SaveChanges();
        return NoContent();
    }

    private static string? Validate(DateOnly date, string? description, WriteUpSeverity severity)
    {
        if (date == default)
        {
            return "A write-up date is required.";
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            return "A description is required.";
        }

        if (description.Trim().Length > MaxDescriptionLength)
        {
            return $"Description is too long ({MaxDescriptionLength} characters max).";
        }

        // JsonStringEnumConverter still accepts a bare number, so an
        // out-of-range value can reach here.
        if (!Enum.IsDefined(severity))
        {
            return "Severity is not valid.";
        }

        return null;
    }

    private Account? FindAccount(int accountId) =>
        db.Accounts.Include(a => a.Location).SingleOrDefault(a => a.Id == accountId);

    private int CallerAccountId() => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private bool IsAdmin(Account account) =>
        (User.IsInRole(nameof(AccountRole.Sa)) || User.IsInRole(nameof(AccountRole.Admin))) && CanAccess(account);

    private bool CanView(Account account) => CallerAccountId() == account.Id || IsAdmin(account);

    private bool CanAccess(Account account) =>
        User.IsInRole(nameof(AccountRole.Sa)) ||
        (account.Location is not null && account.Location.LocationCode == CallerLocationCode());

    private string? CallerLocationCode() =>
        User.FindFirst(TokenService.LocationCodeClaimType)?.Value;

    private static WriteUpDto ToDto(WriteUp w) => new(
        w.Id,
        w.AccountId,
        w.Date,
        w.Description,
        w.Severity,
        w.CreatedByAccountId,
        w.CreatedByAccount is null ? string.Empty : $"{w.CreatedByAccount.FirstName} {w.CreatedByAccount.LastName}",
        w.CreatedAt);
}
