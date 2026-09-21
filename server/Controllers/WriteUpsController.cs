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

// Write-ups / warnings recorded against one employee.
//
// Who can do what:
//  - Any signed-in employee can list their OWN write-ups and acknowledge
//    them ("I received this" — not "I agree").
//  - An Admin/Sa, for an employee in their own location, can list, create,
//    edit, void, and record that the employee declined to acknowledge.
//    They can NOT do any of that for their own account — a write-up about
//    yourself has to come from someone else (a different Admin, or Sa).
//  - Leads get no special access: a write-up is HR-sensitive, so it isn't
//    something to hand out by default.
// Lookups that fail the access check return 404 rather than 403, so the API
// doesn't confirm that another employee's account exists.
//
// Write-ups are never deleted — a mistaken or rescinded one is voided with a
// reason, and every change is appended to the write-up's audit trail
// (WriteUpEvent). Editing a write-up the employee had already responded to
// puts it back to Pending, because what they acknowledged has changed.
[ApiController]
[Route("api/accounts/{accountId:int}/write-ups")]
[Authorize]
public class WriteUpsController(AppDbContext db) : ControllerBase
{
    public const int MaxDescriptionLength = 2000;
    public const int MaxVoidReasonLength = 500;

    [HttpGet]
    public ActionResult<IEnumerable<WriteUpDto>> GetAll(int accountId)
    {
        var account = FindAccount(accountId);
        if (account is null || !CanView(account))
        {
            return NotFound();
        }

        var forManager = CanManage(account);

        // Events are loaded for the employee too, though they never see them:
        // it's how the write-up's current signature is found. Only a manager
        // needs to know who did each one.
        var query = db.WriteUps.Include(w => w.CreatedByAccount).Where(w => w.AccountId == accountId);
        query = forManager
            ? query.Include(w => w.Events).ThenInclude(e => e.ByAccount)
            : query.Include(w => w.Events);

        var writeUps = query
            .OrderByDescending(w => w.Date)
            .ThenByDescending(w => w.Id)
            .ToList();

        return Ok(writeUps.Select(w => ToDto(w, forManager)));
    }

    [HttpPost]
    [Authorize(Policy = "AdminOrAbove")]
    public ActionResult<WriteUpDto> Create(int accountId, CreateWriteUpRequest request)
    {
        var account = FindAccount(accountId);
        if (account is null || !CanManage(account))
        {
            return NotFound();
        }

        var severity = request.Severity ?? WriteUpSeverity.Normal;
        var type = request.Type ?? WriteUpType.Written;
        var validationError = Validate(request.Date, request.Description, severity, type);
        if (validationError is not null)
        {
            return BadRequest(validationError);
        }

        var caller = Caller();
        var writeUp = new WriteUp
        {
            AccountId = accountId,
            Date = request.Date,
            Description = request.Description.Trim(),
            Severity = severity,
            Type = type,
            CreatedByAccountId = caller.Id,
            CreatedByAccount = caller,
            CreatedAt = DateTime.UtcNow,
        };
        AddEvent(writeUp, WriteUpEventAction.Created, null);

        db.WriteUps.Add(writeUp);
        db.SaveChanges();

        return CreatedAtAction(nameof(GetAll), new { accountId }, ToDto(writeUp, forManager: true));
    }

    [HttpPut("{writeUpId:int}")]
    [Authorize(Policy = "AdminOrAbove")]
    public ActionResult<WriteUpDto> Update(int accountId, int writeUpId, UpdateWriteUpRequest request)
    {
        var account = FindAccount(accountId);
        if (account is null || !CanManage(account))
        {
            return NotFound();
        }

        var writeUp = FindWriteUp(accountId, writeUpId);
        if (writeUp is null)
        {
            return NotFound();
        }

        if (writeUp.IsVoided)
        {
            return Conflict("A voided write-up can't be edited.");
        }

        var validationError = Validate(request.Date, request.Description, request.Severity, request.Type);
        if (validationError is not null)
        {
            return BadRequest(validationError);
        }

        var description = request.Description.Trim();
        var changes = new List<string>();
        if (request.Date != writeUp.Date)
        {
            changes.Add($"Date: {writeUp.Date:yyyy-MM-dd} → {request.Date:yyyy-MM-dd}");
        }

        if (request.Type != writeUp.Type)
        {
            changes.Add($"Type: {writeUp.Type} → {request.Type}");
        }

        if (request.Severity != writeUp.Severity)
        {
            changes.Add($"Severity: {writeUp.Severity} → {request.Severity}");
        }

        if (description != writeUp.Description)
        {
            changes.Add($"Description: \"{writeUp.Description}\" → \"{description}\"");
        }

        // Nothing actually changed: no audit entry, and no reason to make the
        // employee acknowledge it again.
        if (changes.Count == 0)
        {
            return Ok(ToDto(writeUp, forManager: true));
        }

        if (writeUp.AcknowledgmentStatus != WriteUpAcknowledgment.Pending)
        {
            changes.Add($"Acknowledgment reset to Pending (was {writeUp.AcknowledgmentStatus}).");
            writeUp.AcknowledgmentStatus = WriteUpAcknowledgment.Pending;
            writeUp.AcknowledgmentAt = null;
            writeUp.AcknowledgmentSignedName = null;
        }

        writeUp.Date = request.Date;
        writeUp.Description = description;
        writeUp.Severity = request.Severity;
        writeUp.Type = request.Type;
        AddEvent(writeUp, WriteUpEventAction.Edited, string.Join("\n", changes));
        db.SaveChanges();

        return Ok(ToDto(writeUp, forManager: true));
    }

    [HttpPost("{writeUpId:int}/void")]
    [Authorize(Policy = "AdminOrAbove")]
    public ActionResult<WriteUpDto> Void(int accountId, int writeUpId, VoidWriteUpRequest request)
    {
        var account = FindAccount(accountId);
        if (account is null || !CanManage(account))
        {
            return NotFound();
        }

        var writeUp = FindWriteUp(accountId, writeUpId);
        if (writeUp is null)
        {
            return NotFound();
        }

        if (writeUp.IsVoided)
        {
            return Conflict("This write-up is already voided.");
        }

        var reason = request.Reason?.Trim();
        if (string.IsNullOrEmpty(reason))
        {
            return BadRequest("A reason is required to void a write-up.");
        }

        if (reason.Length > MaxVoidReasonLength)
        {
            return BadRequest($"Reason is too long ({MaxVoidReasonLength} characters max).");
        }

        writeUp.VoidedAt = DateTime.UtcNow;
        writeUp.VoidReason = reason;
        AddEvent(writeUp, WriteUpEventAction.Voided, reason);
        db.SaveChanges();

        return Ok(ToDto(writeUp, forManager: true));
    }

    // Only the employee the write-up is about can acknowledge it — an admin
    // acknowledging on their behalf would defeat the point (an admin who
    // hand-delivered it and the employee refused uses Decline instead). They
    // confirm by typing their full name (which has to match their account) and
    // drawing a signature, and both are stored: that makes acknowledging a
    // deliberate act, with the signature kept on record.
    [HttpPost("{writeUpId:int}/acknowledge")]
    public ActionResult<WriteUpDto> Acknowledge(int accountId, int writeUpId, AcknowledgeWriteUpRequest request)
    {
        var account = FindAccount(accountId);
        if (account is null || CallerAccountId() != account.Id)
        {
            return NotFound();
        }

        var writeUp = FindWriteUp(accountId, writeUpId);
        if (writeUp is null)
        {
            return NotFound();
        }

        if (writeUp.IsVoided)
        {
            return Conflict("A voided write-up can't be acknowledged.");
        }

        // Idempotent: a double-click or a retry shouldn't add a second entry
        // or move the original timestamp.
        if (writeUp.AcknowledgmentStatus == WriteUpAcknowledgment.Acknowledged)
        {
            return Ok(ToDto(writeUp, forManager: false));
        }

        var typedName = NormalizeName(request.TypedName);
        if (typedName.Length == 0)
        {
            return BadRequest("Type your full name to acknowledge this write-up.");
        }

        var expectedName = NormalizeName($"{account.FirstName} {account.LastName}");
        if (!string.Equals(typedName, expectedName, StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest($"The name doesn't match your account. Type your full name as \"{expectedName}\".");
        }

        if (!SignaturePng.TryParse(request.Signature, out var png, out var signatureError))
        {
            return BadRequest(signatureError);
        }

        var now = DateTime.UtcNow;
        var signature = new WriteUpSignature { WriteUp = writeUp, SignedName = typedName, SignedAt = now, Png = png };
        writeUp.Signatures.Add(signature);

        writeUp.AcknowledgmentStatus = WriteUpAcknowledgment.Acknowledged;
        writeUp.AcknowledgmentAt = now;
        writeUp.AcknowledgmentSignedName = typedName;
        AddEvent(writeUp, WriteUpEventAction.Acknowledged, $"Signed: {typedName}", signature);
        db.SaveChanges();

        return Ok(ToDto(writeUp, forManager: false));
    }

    // Records that the employee refused to acknowledge. They can still
    // acknowledge later (that just replaces Declined).
    [HttpPost("{writeUpId:int}/decline")]
    [Authorize(Policy = "AdminOrAbove")]
    public ActionResult<WriteUpDto> DeclineAcknowledgment(int accountId, int writeUpId)
    {
        var account = FindAccount(accountId);
        if (account is null || !CanManage(account))
        {
            return NotFound();
        }

        var writeUp = FindWriteUp(accountId, writeUpId);
        if (writeUp is null)
        {
            return NotFound();
        }

        if (writeUp.IsVoided)
        {
            return Conflict("A voided write-up can't be changed.");
        }

        if (writeUp.AcknowledgmentStatus == WriteUpAcknowledgment.Acknowledged)
        {
            return Conflict("The employee has already acknowledged this write-up.");
        }

        if (writeUp.AcknowledgmentStatus == WriteUpAcknowledgment.Declined)
        {
            return Ok(ToDto(writeUp, forManager: true));
        }

        writeUp.AcknowledgmentStatus = WriteUpAcknowledgment.Declined;
        writeUp.AcknowledgmentAt = DateTime.UtcNow;
        AddEvent(writeUp, WriteUpEventAction.AcknowledgmentDeclined, null);
        db.SaveChanges();

        return Ok(ToDto(writeUp, forManager: true));
    }

    // The signature image itself. Same access as the write-up: the employee it
    // belongs to, or an admin over them. It has to be one of this write-up's
    // own signatures, so ids can't be used to reach someone else's.
    [HttpGet("{writeUpId:int}/signatures/{signatureId:int}")]
    public IActionResult GetSignature(int accountId, int writeUpId, int signatureId)
    {
        var account = FindAccount(accountId);
        if (account is null || !CanView(account))
        {
            return NotFound();
        }

        var png = db.WriteUpSignatures
            .Where(s => s.Id == signatureId && s.WriteUpId == writeUpId && s.WriteUp!.AccountId == accountId)
            .Select(s => s.Png)
            .SingleOrDefault();
        if (png is null)
        {
            return NotFound();
        }

        // Stored bytes are only ever served back as an image, never sniffed
        // into anything else.
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.CacheControl = "private, no-store";
        return File(png, "image/png");
    }

    private static string? Validate(DateOnly date, string? description, WriteUpSeverity severity, WriteUpType type)
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

        if (!Enum.IsDefined(type))
        {
            return "Type is not valid.";
        }

        return null;
    }

    // Collapses any run of whitespace to one space and trims, so stray or
    // doubled spaces don't make an otherwise correct name fail to match.
    private static string NormalizeName(string? name) =>
        string.Join(' ', (name ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private void AddEvent(WriteUp writeUp, WriteUpEventAction action, string? detail, WriteUpSignature? signature = null)
    {
        var caller = Caller();
        writeUp.Events.Add(new WriteUpEvent
        {
            Action = action,
            ByAccountId = caller.Id,
            ByAccount = caller,
            At = DateTime.UtcNow,
            Detail = detail,
            Signature = signature,
        });
    }

    private Account? FindAccount(int accountId) =>
        db.Accounts.Include(a => a.Location).SingleOrDefault(a => a.Id == accountId);

    private WriteUp? FindWriteUp(int accountId, int writeUpId) =>
        db.WriteUps
            .Include(w => w.CreatedByAccount)
            .Include(w => w.Events).ThenInclude(e => e.ByAccount)
            .SingleOrDefault(w => w.Id == writeUpId && w.AccountId == accountId);

    private Account? cachedCaller;

    private Account Caller() => cachedCaller ??= db.Accounts.Find(CallerAccountId())!;

    private int CallerAccountId() => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    // Admin/Sa over an employee in their location — but never over their own
    // account.
    private bool CanManage(Account account) =>
        (User.IsInRole(nameof(AccountRole.Sa)) || User.IsInRole(nameof(AccountRole.Admin))) &&
        CanAccess(account) &&
        CallerAccountId() != account.Id;

    private bool CanView(Account account) => CallerAccountId() == account.Id || CanManage(account);

    private bool CanAccess(Account account) =>
        User.IsInRole(nameof(AccountRole.Sa)) ||
        (account.Location is not null && account.Location.LocationCode == CallerLocationCode());

    private string? CallerLocationCode() =>
        User.FindFirst(TokenService.LocationCodeClaimType)?.Value;

    private static string NameOf(Account? account) =>
        account is null ? string.Empty : $"{account.FirstName} {account.LastName}";

    // The signature on the most recent acknowledgment — but only while it is
    // still acknowledged: an edit resets the write-up to Pending, and the old
    // signature then lives on only in the history.
    private static int? CurrentSignatureId(WriteUp w) =>
        w.AcknowledgmentStatus != WriteUpAcknowledgment.Acknowledged
            ? null
            : w.Events
                .Where(e => e.Action == WriteUpEventAction.Acknowledged && e.SignatureId is not null)
                .OrderByDescending(e => e.Id)
                .Select(e => e.SignatureId)
                .FirstOrDefault();

    private static WriteUpDto ToDto(WriteUp w, bool forManager) => new(
        w.Id,
        w.AccountId,
        w.Date,
        w.Description,
        w.Severity,
        w.Type,
        w.CreatedByAccountId,
        NameOf(w.CreatedByAccount),
        w.CreatedAt,
        w.AcknowledgmentStatus,
        w.AcknowledgmentAt,
        w.AcknowledgmentSignedName,
        CurrentSignatureId(w),
        w.IsVoided,
        w.VoidedAt,
        forManager ? w.VoidReason : null,
        forManager
            ? w.Events
                .OrderBy(e => e.At)
                .ThenBy(e => e.Id)
                .Select(e => new WriteUpEventDto(e.Id, e.Action, e.ByAccountId, NameOf(e.ByAccount), e.At, e.Detail, e.SignatureId))
                .ToList()
            : null);
}
