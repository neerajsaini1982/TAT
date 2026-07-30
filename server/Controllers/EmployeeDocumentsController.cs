using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Server.Data;
using Server.Dtos;
using Server.Models;
using Server.Security;

namespace Server.Controllers;

// Onboarding documents for one employee, stored on local disk under
// DocumentsRoot/{accountId}/{guid}{ext} — the on-disk name is always a GUID,
// never derived from the uploaded filename, so there's no path-traversal or
// collision risk; the human-readable name only ever lives in the DB.
[ApiController]
[Route("api/accounts/{accountId:int}/documents")]
[Authorize]
public class EmployeeDocumentsController(AppDbContext db, IConfiguration config) : ControllerBase
{
    private const long MaxFileSizeBytes = 20 * 1024 * 1024;
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".doc", ".docx", ".jpg", ".jpeg", ".png",
    };

    [HttpGet]
    public ActionResult<IEnumerable<EmployeeDocumentDto>> GetAll(int accountId)
    {
        var account = db.Accounts.Include(a => a.Location).SingleOrDefault(a => a.Id == accountId);
        if (account is null || !CanView(account))
        {
            return NotFound();
        }

        var docs = db.EmployeeDocuments
            .Include(d => d.UploadedByAccount)
            .Where(d => d.AccountId == accountId)
            .OrderByDescending(d => d.UploadedAt)
            .ToList();

        return Ok(docs.Select(ToDto));
    }

    [HttpPost]
    public async Task<ActionResult<EmployeeDocumentDto>> Upload(int accountId, [FromForm] string name, IFormFile file)
    {
        var account = db.Accounts.Include(a => a.Location).SingleOrDefault(a => a.Id == accountId);
        if (account is null || !CanView(account))
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return BadRequest("A document name is required.");
        }

        var validationError = ValidateFile(file);
        if (validationError is not null)
        {
            return BadRequest(validationError);
        }

        var extension = Path.GetExtension(file.FileName);
        var storedFileName = $"{Guid.NewGuid():N}{extension}";
        var accountFolder = Path.Combine(ResolveDocumentsRoot(), accountId.ToString());
        Directory.CreateDirectory(accountFolder);

        await using (var stream = System.IO.File.Create(Path.Combine(accountFolder, storedFileName)))
        {
            await file.CopyToAsync(stream);
        }

        var callerId = CallerAccountId();
        var document = new EmployeeDocument
        {
            AccountId = accountId,
            Name = name.Trim(),
            FileName = file.FileName,
            StoredFileName = storedFileName,
            ContentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType,
            FileSizeBytes = file.Length,
            UploadedByAccountId = callerId,
        };

        db.EmployeeDocuments.Add(document);
        db.SaveChanges();

        document.UploadedByAccount = db.Accounts.Find(callerId);
        return CreatedAtAction(nameof(GetAll), new { accountId }, ToDto(document));
    }

    [HttpGet("{documentId:int}/download")]
    public IActionResult Download(int accountId, int documentId)
    {
        var account = db.Accounts.Include(a => a.Location).SingleOrDefault(a => a.Id == accountId);
        if (account is null || !CanView(account))
        {
            return NotFound();
        }

        var document = db.EmployeeDocuments.SingleOrDefault(d => d.Id == documentId && d.AccountId == accountId);
        if (document is null)
        {
            return NotFound();
        }

        var path = Path.Combine(ResolveDocumentsRoot(), accountId.ToString(), document.StoredFileName);
        if (!System.IO.File.Exists(path))
        {
            return NotFound();
        }

        return PhysicalFile(path, document.ContentType, document.FileName);
    }

    [HttpPut("{documentId:int}")]
    public ActionResult<EmployeeDocumentDto> Rename(int accountId, int documentId, UpdateEmployeeDocumentRequest request)
    {
        var account = db.Accounts.Include(a => a.Location).SingleOrDefault(a => a.Id == accountId);
        if (account is null || !CanView(account))
        {
            return NotFound();
        }

        var document = db.EmployeeDocuments
            .Include(d => d.UploadedByAccount)
            .SingleOrDefault(d => d.Id == documentId && d.AccountId == accountId);
        if (document is null)
        {
            return NotFound();
        }

        var callerId = CallerAccountId();
        if (document.UploadedByAccountId != callerId && !IsAdmin(account))
        {
            return Forbid();
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest("A document name is required.");
        }

        document.Name = request.Name.Trim();
        db.SaveChanges();

        return Ok(ToDto(document));
    }

    // Only Admin/Sa can delete — non-admins (including the employee who
    // uploaded it) cannot, per the issue's requirement.
    [HttpDelete("{documentId:int}")]
    [Authorize(Policy = "AdminOrAbove")]
    public IActionResult Delete(int accountId, int documentId)
    {
        var account = db.Accounts.Include(a => a.Location).SingleOrDefault(a => a.Id == accountId);
        if (account is null || !IsAdmin(account))
        {
            return NotFound();
        }

        var document = db.EmployeeDocuments.SingleOrDefault(d => d.Id == documentId && d.AccountId == accountId);
        if (document is null)
        {
            return NotFound();
        }

        var path = Path.Combine(ResolveDocumentsRoot(), accountId.ToString(), document.StoredFileName);
        try
        {
            System.IO.File.Delete(path);
        }
        catch (IOException)
        {
            // Best-effort: the DB row is still removed even if the file is
            // already gone or locked.
        }

        db.EmployeeDocuments.Remove(document);
        db.SaveChanges();
        return NoContent();
    }

    private static string? ValidateFile(IFormFile? file)
    {
        if (file is null || file.Length == 0)
        {
            return "Choose a file to upload.";
        }

        if (file.Length > MaxFileSizeBytes)
        {
            return "File is too large (20 MB max).";
        }

        if (!AllowedExtensions.Contains(Path.GetExtension(file.FileName)))
        {
            return "Only PDF, Word, and image (JPG/PNG) files are supported.";
        }

        return null;
    }

    private int CallerAccountId() => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private bool IsAdmin(Account account) =>
        (User.IsInRole(nameof(AccountRole.Sa)) || User.IsInRole(nameof(AccountRole.Admin))) && CanAccess(account);

    private bool CanView(Account account) => CallerAccountId() == account.Id || IsAdmin(account);

    private bool CanAccess(Account account) =>
        User.IsInRole(nameof(AccountRole.Sa)) ||
        (account.Location is not null && account.Location.LocationCode == CallerLocationCode());

    private string? CallerLocationCode() =>
        User.FindFirst(TokenService.LocationCodeClaimType)?.Value;

    private string ResolveDocumentsRoot() =>
        config["Storage:DocumentsRoot"]
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TAT", "documents");

    private static EmployeeDocumentDto ToDto(EmployeeDocument d) => new(
        d.Id,
        d.AccountId,
        d.Name,
        d.FileName,
        d.ContentType,
        d.FileSizeBytes,
        d.UploadedAt.ToString("O"),
        d.UploadedByAccountId,
        d.UploadedByAccount is null ? string.Empty : $"{d.UploadedByAccount.FirstName} {d.UploadedByAccount.LastName}");
}
