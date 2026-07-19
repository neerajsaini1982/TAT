using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Server.Data;
using Server.Dtos;
using Server.Models;
using Server.Security;
using Server.Services;

namespace Server.Controllers;

[ApiController]
[Route("api/accounts")]
public class AccountsController(AppDbContext db, IEmailSender emailSender) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = "AdminOrAbove")]
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
    [Authorize(Policy = "AdminOrAbove")]
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
    [Authorize(Policy = "AdminOrAbove")]
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

        string username;
        string passwordHash;

        // Employees log in with a UserCode only (see AuthController.EmployeeLogin),
        // so they don't need a username or password of their own.
        if (request.Role == AccountRole.Employee)
        {
            username = AccountProvisioning.GenerateUniqueUsername(db, request.FirstName, request.LastName);
            passwordHash = PasswordHasher.Hash(Guid.NewGuid().ToString("N"));
        }
        else
        {
            if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            {
                return BadRequest("Username and password are required for this role.");
            }

            if (db.Accounts.Any(a => a.Username == request.Username))
            {
                return Conflict($"Username '{request.Username}' is already in use.");
            }

            username = request.Username;
            passwordHash = PasswordHasher.Hash(request.Password);
        }

        var account = new Account
        {
            Username = username,
            PasswordHash = passwordHash,
            FirstName = request.FirstName,
            LastName = request.LastName,
            Email = request.Email,
            Phone = request.Phone,
            Role = request.Role,
            IsActive = true,
            LocationId = location?.Id,
            UserCode = location is null ? null : AccountProvisioning.GenerateUniqueUserCode(db, location.Id),
        };

        db.Accounts.Add(account);
        db.SaveChanges();

        account.Location = location;
        return CreatedAtAction(nameof(Get), new { id = account.Id }, ToDto(account));
    }

    // Lets an admin regenerate a lost/leaked user code for one of their accounts.
    [HttpPost("{id:int}/reset-code")]
    [Authorize(Policy = "AdminOrAbove")]
    public ActionResult<AccountDto> ResetCode(int id)
    {
        var account = db.Accounts.Include(a => a.Location).SingleOrDefault(a => a.Id == id);
        if (account is null || !CanAccess(account))
        {
            return NotFound();
        }

        if (account.LocationId is null)
        {
            return BadRequest("This account has no location and therefore no user code.");
        }

        account.UserCode = AccountProvisioning.GenerateUniqueUserCode(db, account.LocationId.Value);
        db.SaveChanges();

        return Ok(ToDto(account));
    }

    // Lets the signed-in account (typically an Employee) reset its own code,
    // e.g. after forgetting or suspecting it's been shared.
    [HttpPost("mine/reset-code")]
    [Authorize]
    public ActionResult<AccountDto> ResetMyCode()
    {
        var accountId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var account = db.Accounts.Include(a => a.Location).SingleOrDefault(a => a.Id == accountId);
        if (account is null || account.LocationId is null)
        {
            return BadRequest("This account has no user code to reset.");
        }

        account.UserCode = AccountProvisioning.GenerateUniqueUserCode(db, account.LocationId.Value);
        db.SaveChanges();

        return Ok(ToDto(account));
    }

    // Emails an Employee their login link and user code, using the
    // LoginCredentials template (custom if the location has saved one, else
    // the built-in default) and this location's SMTP settings. The login
    // link itself is computed by the caller (it already knows its own
    // origin) rather than the server guessing its hostname.
    [HttpPost("{id:int}/send-credentials")]
    [Authorize(Policy = "AdminOrAbove")]
    public async Task<IActionResult> SendCredentials(int id, SendCredentialsRequest request)
    {
        var account = db.Accounts.Include(a => a.Location).SingleOrDefault(a => a.Id == id);
        if (account is null || !CanAccess(account))
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(account.Email))
        {
            return BadRequest("This employee has no email address on file.");
        }

        if (account.LocationId is null || string.IsNullOrEmpty(account.UserCode))
        {
            return BadRequest("This account has no user code to send.");
        }

        var settings = db.LocationSettings.SingleOrDefault(s => s.LocationId == account.LocationId);
        if (settings is null)
        {
            return BadRequest("SMTP is not configured for this location. Set it up under Settings first.");
        }

        var template = db.EmailTemplates.SingleOrDefault(
            t => t.LocationId == account.LocationId && t.Key == EmailTemplateKeys.LoginCredentials)
            ?? EmailTemplateCatalog.Default(EmailTemplateKeys.LoginCredentials);

        var placeholders = new Dictionary<string, string>
        {
            ["{{employeeName}}"] = $"{account.FirstName} {account.LastName}",
            ["{{locationName}}"] = account.Location?.Name ?? string.Empty,
            ["{{userCode}}"] = account.UserCode,
            ["{{loginLink}}"] = request.LoginLink,
        };

        try
        {
            await emailSender.SendAsync(
                settings,
                account.Email,
                Render(template.Subject, placeholders),
                Render(template.BodyHtml, placeholders));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status502BadGateway, "Failed to send email. Check the SMTP settings and try again.");
        }

        return NoContent();
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = "AdminOrAbove")]
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
    [Authorize(Policy = "AdminOrAbove")]
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

    private static string Render(string template, Dictionary<string, string> placeholders)
    {
        foreach (var (token, value) in placeholders)
        {
            template = template.Replace(token, value);
        }

        return template;
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
        a.Location?.LocationCode,
        a.BirthDate,
        a.JobTitle,
        a.Address1,
        a.Address2,
        a.City,
        a.State,
        a.Zipcode,
        a.Supervisor,
        a.AdpStatus);
}
