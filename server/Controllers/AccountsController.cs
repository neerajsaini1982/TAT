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
public class AccountsController(AppDbContext db, IEmailSender emailSender, SsnProtector ssnProtector, IConfiguration config) : ControllerBase
{
    private const long MaxPhotoSizeBytes = 5 * 1024 * 1024;
    private static readonly HashSet<string> AllowedPhotoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png",
    };

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

        var (ssnOk, ssnDigits, ssnError) = NormalizeSsn(request.Ssn);
        if (!ssnOk)
        {
            return BadRequest(ssnError);
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
            IsOnShiftSchedule = true,
            LocationId = location?.Id,
            UserCode = location is null ? null : AccountProvisioning.GenerateUniqueUserCode(db, location.Id),
            HourlyRate = request.HourlyRate,
            DateOfBirth = request.DateOfBirth,
            HireDate = request.HireDate,
            EmploymentType = request.EmploymentType,
            SsnEncrypted = ssnDigits is null ? null : ssnProtector.Protect(ssnDigits),
            SsnLast4 = ssnDigits is null ? null : ssnDigits[^4..],
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

    // Lets the signed-in account view its own profile — used by the
    // Employee portal's Account menu, which only that account itself can
    // reach (AdminOrAbove would 403 an Employee).
    [HttpGet("mine")]
    [Authorize]
    public ActionResult<AccountDto> GetMine()
    {
        var account = db.Accounts.Include(a => a.Location).SingleOrDefault(a => a.Id == CallerAccountId());
        if (account is null)
        {
            return NotFound();
        }

        return Ok(ToDto(account));
    }

    // Lets the signed-in account update its own email/phone — the only
    // fields an Employee is allowed to change about themselves.
    [HttpPut("mine")]
    [Authorize]
    public ActionResult<AccountDto> UpdateMine(UpdateMineRequest request)
    {
        var account = db.Accounts.Include(a => a.Location).SingleOrDefault(a => a.Id == CallerAccountId());
        if (account is null)
        {
            return NotFound();
        }

        account.Email = request.Email;
        account.Phone = request.Phone;
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
                EmailTemplateCatalog.Render(template.Subject, placeholders),
                EmailTemplateCatalog.Render(template.BodyHtml, placeholders));
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

        if ((request.Role == AccountRole.Sa || account.Role == AccountRole.Sa) && !User.IsInRole(nameof(AccountRole.Sa)))
        {
            return Forbid();
        }

        var (ssnOk, ssnDigits, ssnError) = NormalizeSsn(request.Ssn);
        if (!ssnOk)
        {
            return BadRequest(ssnError);
        }

        // Promoting away from Employee: the account's PasswordHash is a
        // random, unknown value generated at creation (Employees log in with
        // a UserCode instead), so real login credentials must be supplied now.
        if (account.Role == AccountRole.Employee && request.Role != AccountRole.Employee)
        {
            if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            {
                return BadRequest("Username and password are required when changing role away from Employee.");
            }

            if (db.Accounts.Any(a => a.Id != id && a.Username == request.Username))
            {
                return Conflict($"Username '{request.Username}' is already in use.");
            }

            account.Username = request.Username;
            account.PasswordHash = PasswordHasher.Hash(request.Password);
        }
        // Renaming a username without a role change — e.g. correcting an
        // Employee's auto-generated username from the ADP import. Doesn't
        // touch PasswordHash: an Employee's is still that unknown random
        // value, irrelevant since they log in with UserCode, not this.
        else if (!string.IsNullOrWhiteSpace(request.Username) && request.Username != account.Username)
        {
            if (db.Accounts.Any(a => a.Id != id && a.Username == request.Username))
            {
                return Conflict($"Username '{request.Username}' is already in use.");
            }

            account.Username = request.Username;
        }

        account.FirstName = request.FirstName;
        account.LastName = request.LastName;
        account.Email = request.Email;
        account.Phone = request.Phone;
        account.IsActive = request.IsActive;
        account.IsOnShiftSchedule = request.IsOnShiftSchedule;
        account.Role = request.Role;
        account.HourlyRate = request.HourlyRate;
        account.DateOfBirth = request.DateOfBirth;
        account.HireDate = request.HireDate;
        account.EmploymentType = request.EmploymentType;
        if (ssnDigits is not null)
        {
            account.SsnEncrypted = ssnProtector.Protect(ssnDigits);
            account.SsnLast4 = ssnDigits[^4..];
        }

        db.SaveChanges();

        return Ok(ToDto(account));
    }

    [HttpGet("{id:int}/photo")]
    [Authorize]
    public IActionResult GetPhoto(int id)
    {
        var account = db.Accounts.Include(a => a.Location).SingleOrDefault(a => a.Id == id);
        if (account is null || !CanViewPhoto(account) || account.PhotoFileName is null)
        {
            return NotFound();
        }

        var path = Path.Combine(ResolvePhotosRoot(), id.ToString(), account.PhotoFileName);
        if (!System.IO.File.Exists(path))
        {
            return NotFound();
        }

        return PhysicalFile(path, account.PhotoContentType ?? "application/octet-stream");
    }

    [HttpPost("{id:int}/photo")]
    [Authorize(Policy = "AdminOrAbove")]
    public async Task<ActionResult<AccountDto>> UploadPhoto(int id, IFormFile file)
    {
        var account = db.Accounts.Include(a => a.Location).SingleOrDefault(a => a.Id == id);
        if (account is null || !CanAccess(account))
        {
            return NotFound();
        }

        var validationError = ValidatePhoto(file);
        if (validationError is not null)
        {
            return BadRequest(validationError);
        }

        var accountFolder = Path.Combine(ResolvePhotosRoot(), id.ToString());
        Directory.CreateDirectory(accountFolder);

        var extension = Path.GetExtension(file.FileName);
        var storedFileName = $"{Guid.NewGuid():N}{extension}";
        await using (var stream = System.IO.File.Create(Path.Combine(accountFolder, storedFileName)))
        {
            await file.CopyToAsync(stream);
        }

        var previousFileName = account.PhotoFileName;
        account.PhotoFileName = storedFileName;
        account.PhotoContentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType;
        db.SaveChanges();

        if (previousFileName is not null)
        {
            TryDeletePhotoFile(accountFolder, previousFileName);
        }

        return Ok(ToDto(account));
    }

    [HttpDelete("{id:int}/photo")]
    [Authorize(Policy = "AdminOrAbove")]
    public ActionResult<AccountDto> DeletePhoto(int id)
    {
        var account = db.Accounts.Include(a => a.Location).SingleOrDefault(a => a.Id == id);
        if (account is null || !CanAccess(account))
        {
            return NotFound();
        }

        if (account.PhotoFileName is not null)
        {
            TryDeletePhotoFile(Path.Combine(ResolvePhotosRoot(), id.ToString()), account.PhotoFileName);
            account.PhotoFileName = null;
            account.PhotoContentType = null;
            db.SaveChanges();
        }

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

    private bool CanViewPhoto(Account account) => CallerAccountId() == account.Id || CanAccess(account);

    private int CallerAccountId() => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private string? CallerLocationCode() =>
        User.FindFirst(TokenService.LocationCodeClaimType)?.Value;

    private string ResolvePhotosRoot() =>
        config["Storage:PhotosRoot"]
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TAT", "photos");

    private static void TryDeletePhotoFile(string folder, string fileName)
    {
        try
        {
            System.IO.File.Delete(Path.Combine(folder, fileName));
        }
        catch (IOException)
        {
            // Best-effort: the DB row is still updated even if the file is
            // already gone or locked.
        }
    }

    private static string? ValidatePhoto(IFormFile? file)
    {
        if (file is null || file.Length == 0)
        {
            return "Choose a photo to upload.";
        }

        if (file.Length > MaxPhotoSizeBytes)
        {
            return "Photo is too large (5 MB max).";
        }

        if (!AllowedPhotoExtensions.Contains(Path.GetExtension(file.FileName)))
        {
            return "Only JPG and PNG photos are supported.";
        }

        return null;
    }

    // null/empty Ssn means "leave unchanged" (Update) or "not provided"
    // (Create) — not an error. Anything else must be exactly 9 digits.
    private static (bool Ok, string? Digits, string? Error) NormalizeSsn(string? ssn)
    {
        if (string.IsNullOrEmpty(ssn))
        {
            return (true, null, null);
        }

        var digits = ssn.Trim();
        if (digits.Length != 9 || !digits.All(char.IsAsciiDigit))
        {
            return (false, null, "SSN must be exactly 9 digits.");
        }

        return (true, digits, null);
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
        a.IsOnShiftSchedule,
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
        a.AdpStatus,
        a.HourlyRate,
        a.SsnLast4 is null ? null : $"***-**-{a.SsnLast4}",
        a.DateOfBirth?.ToString("yyyy-MM-dd"),
        a.HireDate?.ToString("yyyy-MM-dd"),
        a.EmploymentType?.ToString(),
        a.PhotoFileName is not null);
}
