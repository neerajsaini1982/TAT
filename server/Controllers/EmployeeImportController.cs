using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Server.Data;
using Server.Dtos;
using Server.Models;
using Server.Security;

namespace Server.Controllers;

// Bulk-imports employees from an ADP "Employee Directory" .xlsx export.
// Two-step preview/commit: /preview never touches the database, just parses
// the file and reports what would happen to each row; /commit creates
// accounts for the rows the admin confirmed. A re-upload never modifies an
// employee already in the system — matched by (FirstName, LastName,
// BirthDate), see DedupeKey.
[ApiController]
[Route("api/employee-import")]
[Authorize(Policy = "AdminOrAbove")]
public class EmployeeImportController(AppDbContext db) : ControllerBase
{
    private const long MaxFileSizeBytes = 5 * 1024 * 1024;

    [HttpPost("preview")]
    public ActionResult<EmployeeImportPreviewResult> Preview(IFormFile file, [FromQuery] string? locationCode)
    {
        var location = ResolveLocation(locationCode);
        if (location is null)
        {
            return BadRequest("A valid locationCode is required.");
        }

        var validationError = ValidateFile(file);
        if (validationError is not null)
        {
            return BadRequest(validationError);
        }

        List<ParsedRow> parsedRows;
        try
        {
            parsedRows = ParseWorkbook(file);
        }
        catch (Exception)
        {
            return BadRequest("Couldn't read that file. Make sure it's an ADP Employee Directory export saved as .xlsx.");
        }

        var existingKeys = ExistingKeys(location.Id);
        var seenInFile = new HashSet<(string, string, string)>();
        var rows = new List<EmployeeImportRowDto>();
        var newCount = 0;
        var skippedCount = 0;

        foreach (var r in parsedRows)
        {
            var key = DedupeKey(r.FirstName, r.LastName, r.BirthDate);
            string? skipReason = null;
            if (existingKeys.Contains(key))
            {
                skipReason = "Already exists";
            }
            else if (!seenInFile.Add(key))
            {
                skipReason = "Duplicate row in file";
            }

            var willCreate = skipReason is null;
            if (willCreate)
            {
                newCount++;
            }
            else
            {
                skippedCount++;
            }

            rows.Add(new EmployeeImportRowDto(
                r.FirstName, r.LastName, r.BirthDate, r.JobTitle, r.Address1, r.Address2,
                r.City, r.State, r.Zipcode, r.Phone, r.Supervisor, r.AdpStatus, r.IsActive,
                willCreate, skipReason));
        }

        return Ok(new EmployeeImportPreviewResult(rows, rows.Count, newCount, skippedCount));
    }

    [HttpPost("commit")]
    public ActionResult<EmployeeImportCommitResult> Commit(EmployeeImportCommitRequest request)
    {
        var location = ResolveLocation(request.LocationCode);
        if (location is null)
        {
            return BadRequest("A valid locationCode is required.");
        }

        // Defensive re-check: the preview may be stale (another admin could
        // have imported since, or a row could have been unchecked).
        var existingKeys = ExistingKeys(location.Id);
        var seenInBatch = new HashSet<(string, string, string)>();
        var created = new List<Account>();
        var skippedCount = 0;

        foreach (var row in request.Rows)
        {
            if (!row.WillCreate)
            {
                continue;
            }

            var key = DedupeKey(row.FirstName, row.LastName, row.BirthDate);
            if (existingKeys.Contains(key) || !seenInBatch.Add(key))
            {
                skippedCount++;
                continue;
            }

            var account = new Account
            {
                Username = AccountProvisioning.GenerateUniqueUsername(db, row.FirstName, row.LastName),
                PasswordHash = PasswordHasher.Hash(Guid.NewGuid().ToString("N")),
                FirstName = row.FirstName,
                LastName = row.LastName,
                Email = string.Empty,
                Phone = row.Phone ?? string.Empty,
                Role = AccountRole.Employee,
                IsActive = row.IsActive,
                LocationId = location.Id,
                UserCode = AccountProvisioning.GenerateUniqueUserCode(db, location.Id),
                BirthDate = row.BirthDate,
                JobTitle = row.JobTitle,
                Address1 = row.Address1,
                Address2 = row.Address2,
                City = row.City,
                State = row.State,
                Zipcode = row.Zipcode,
                Supervisor = row.Supervisor,
                AdpStatus = row.AdpStatus,
            };

            db.Accounts.Add(account);
            created.Add(account);
        }

        db.SaveChanges();

        foreach (var a in created)
        {
            a.Location = location;
        }

        return Ok(new EmployeeImportCommitResult(created.Count, skippedCount, created.Select(ToAccountDto).ToList()));
    }

    private static string? ValidateFile(IFormFile? file)
    {
        if (file is null || file.Length == 0)
        {
            return "Choose a file to upload.";
        }

        if (!file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            return "Only .xlsx files are supported.";
        }

        if (file.Length > MaxFileSizeBytes)
        {
            return "File is too large (5 MB max).";
        }

        return null;
    }

    // ADP's report has a variable-length metadata block (company name,
    // report name, filters) above the real header row, so this scans for
    // the row whose first cell literally reads "Employee" rather than
    // assuming a fixed row number.
    private static List<ParsedRow> ParseWorkbook(IFormFile file)
    {
        using var stream = file.OpenReadStream();
        using var workbook = new XLWorkbook(stream);
        var worksheet = workbook.Worksheets.First();

        var headerRow = worksheet.RowsUsed()
            .FirstOrDefault(r => string.Equals(r.Cell(1).GetString().Trim(), "Employee", StringComparison.OrdinalIgnoreCase));
        if (headerRow is null)
        {
            throw new InvalidOperationException("Header row not found.");
        }

        var rows = new List<ParsedRow>();
        var currentRow = headerRow.RowBelow();
        while (!currentRow.IsEmpty() && !string.IsNullOrWhiteSpace(currentRow.Cell(1).GetString()))
        {
            var employee = currentRow.Cell(1).GetString().Trim();
            var commaIndex = employee.IndexOf(',');
            var lastName = commaIndex >= 0 ? employee[..commaIndex].Trim() : string.Empty;
            var firstName = commaIndex >= 0 ? employee[(commaIndex + 1)..].Trim() : employee;

            var status = currentRow.Cell(10).GetString().Trim();

            rows.Add(new ParsedRow(
                firstName,
                lastName,
                NullIfEmpty(currentRow.Cell(2).GetString()),
                NullIfEmpty(currentRow.Cell(3).GetString()),
                NullIfEmpty(currentRow.Cell(4).GetString()),
                NullIfEmpty(currentRow.Cell(5).GetString()),
                NullIfEmpty(currentRow.Cell(6).GetString()),
                NullIfEmpty(currentRow.Cell(7).GetString()),
                NullIfEmpty(currentRow.Cell(8).GetString()),
                CleanPhone(currentRow.Cell(9).GetString()),
                NullIfEmpty(currentRow.Cell(11).GetString()),
                NullIfEmpty(status),
                string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase)));

            currentRow = currentRow.RowBelow();
        }

        return rows;
    }

    // ADP's phone column packs multiple numbers/labels onto separate lines
    // with trailing tab runs, e.g. "3502182806  (M)\n\t\t\t...\t\t\t" — keep
    // just the first line, which is the primary number and its type label.
    private static string? CleanPhone(string raw)
    {
        var firstLine = raw.Split('\n').FirstOrDefault();
        return string.IsNullOrWhiteSpace(firstLine) ? null : firstLine.Trim();
    }

    private static string? NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private HashSet<(string, string, string)> ExistingKeys(int locationId) =>
        db.Accounts
            .Where(a => a.LocationId == locationId && a.BirthDate != null)
            .Select(a => new { a.FirstName, a.LastName, a.BirthDate })
            .AsEnumerable()
            .Select(a => DedupeKey(a.FirstName, a.LastName, a.BirthDate))
            .ToHashSet();

    private static (string, string, string) DedupeKey(string firstName, string lastName, string? birthDate) =>
        (firstName.Trim().ToLowerInvariant(), lastName.Trim().ToLowerInvariant(), (birthDate ?? string.Empty).Trim());

    private Location? ResolveLocation(string? locationCode)
    {
        if (User.IsInRole(nameof(AccountRole.Sa)))
        {
            return string.IsNullOrWhiteSpace(locationCode)
                ? null
                : db.Locations.SingleOrDefault(l => l.LocationCode == locationCode);
        }

        var callerLocationCode = CallerLocationCode();
        return db.Locations.SingleOrDefault(l => l.LocationCode == callerLocationCode);
    }

    private string? CallerLocationCode() =>
        User.FindFirst(TokenService.LocationCodeClaimType)?.Value;

    private static AccountDto ToAccountDto(Account a) => new(
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

    private sealed record ParsedRow(
        string FirstName,
        string LastName,
        string? BirthDate,
        string? JobTitle,
        string? Address1,
        string? Address2,
        string? City,
        string? State,
        string? Zipcode,
        string? Phone,
        string? Supervisor,
        string? AdpStatus,
        bool IsActive);
}
