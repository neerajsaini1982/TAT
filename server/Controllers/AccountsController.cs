using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Server.Data;
using Server.Dtos;
using Server.Models;
using Server.Security;

namespace Server.Controllers;

[ApiController]
[Route("api/accounts")]
[Authorize(Policy = "AdminOrAbove")]
public class AccountsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public ActionResult<IEnumerable<AccountDto>> GetAll([FromQuery] string? locationCode)
    {
        var query = db.Accounts.Include(a => a.Location).AsQueryable();

        if (User.IsInRole(nameof(AccountRole.Sa)))
        {
            if (!string.IsNullOrWhiteSpace(locationCode))
            {
                query = query.Where(a => a.Location != null && a.Location.LocationCode == locationCode);
            }
        }
        else
        {
            // Admin: always scoped to their own location, regardless of what
            // the client asks for.
            var callerLocationCode = CallerLocationCode();
            query = query.Where(a => a.Location != null && a.Location.LocationCode == callerLocationCode);
        }

        return Ok(query.OrderBy(a => a.Username).Select(ToDto));
    }

    [HttpGet("{id:int}")]
    public ActionResult<AccountDto> Get(int id)
    {
        var account = db.Accounts.Include(a => a.Location).SingleOrDefault(a => a.Id == id);
        if (account is null || !CanAccess(account))
        {
            return NotFound();
        }

        return Ok(ToDto(account));
    }

    [HttpPost]
    public ActionResult<AccountDto> Create(CreateAccountRequest request)
    {
        if (request.Role == AccountRole.Sa && !User.IsInRole(nameof(AccountRole.Sa)))
        {
            return Forbid();
        }

        Location? location = null;
        if (request.Role != AccountRole.Sa)
        {
            var locationId = User.IsInRole(nameof(AccountRole.Sa))
                ? request.LocationId
                : db.Locations.Single(l => l.LocationCode == CallerLocationCode()).Id;

            location = locationId.HasValue ? db.Locations.Find(locationId.Value) : null;
            if (location is null)
            {
                return BadRequest("A valid locationId is required for non-Sa accounts.");
            }
        }

        if (db.Accounts.Any(a => a.Username == request.Username))
        {
            return Conflict($"Username '{request.Username}' is already in use.");
        }

        var account = new Account
        {
            Username = request.Username,
            PasswordHash = PasswordHasher.Hash(request.Password),
            FirstName = request.FirstName,
            LastName = request.LastName,
            Email = request.Email,
            Phone = request.Phone,
            Role = request.Role,
            IsActive = true,
            LocationId = location?.Id,
            UserCode = location is null ? null : GenerateUniqueUserCode(location.Id),
        };

        db.Accounts.Add(account);
        db.SaveChanges();

        account.Location = location;
        return CreatedAtAction(nameof(Get), new { id = account.Id }, ToDto(account));
    }

    [HttpPut("{id:int}")]
    public ActionResult<AccountDto> Update(int id, UpdateAccountRequest request)
    {
        var account = db.Accounts.Include(a => a.Location).SingleOrDefault(a => a.Id == id);
        if (account is null || !CanAccess(account))
        {
            return NotFound();
        }

        account.FirstName = request.FirstName;
        account.LastName = request.LastName;
        account.Email = request.Email;
        account.Phone = request.Phone;
        account.IsActive = request.IsActive;
        db.SaveChanges();

        return Ok(ToDto(account));
    }

    [HttpDelete("{id:int}")]
    public IActionResult Delete(int id)
    {
        var account = db.Accounts.Include(a => a.Location).SingleOrDefault(a => a.Id == id);
        if (account is null || !CanAccess(account))
        {
            return NotFound();
        }

        db.Accounts.Remove(account);
        db.SaveChanges();
        return NoContent();
    }

    private bool CanAccess(Account account) =>
        User.IsInRole(nameof(AccountRole.Sa)) ||
        (account.Location is not null && account.Location.LocationCode == CallerLocationCode());

    private string? CallerLocationCode() =>
        User.FindFirst(TokenService.LocationCodeClaimType)?.Value;

    private string GenerateUniqueUserCode(int locationId)
    {
        string code;
        do
        {
            code = Random.Shared.Next(0, 1_000_000).ToString("D6");
        } while (db.Accounts.Any(a => a.LocationId == locationId && a.UserCode == code));

        return code;
    }

    private static AccountDto ToDto(Account a) => new(
        a.Id,
        a.Username,
        a.FirstName,
        a.LastName,
        a.Email,
        a.Phone,
        a.Role.ToString(),
        a.IsActive,
        a.UserCode,
        a.Location?.LocationCode);
}
