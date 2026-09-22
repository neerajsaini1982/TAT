using Server.Models;

namespace Server.Dtos;

public record WriteUpEventDto(
    int Id,
    WriteUpEventAction Action,
    int ByAccountId,
    string ByName,
    DateTime At,
    string? Detail,
    int? SignatureId);

// VoidReason and History are only filled in for someone managing the
// write-up (an Admin/Sa other than the employee themselves) — the employee
// sees the state (acknowledgment, whether it was voided) but not the
// internal reasoning or the edit trail.
public record WriteUpDto(
    int Id,
    int AccountId,
    DateOnly Date,
    string Description,
    WriteUpSeverity Severity,
    WriteUpType Type,
    int CreatedByAccountId,
    string CreatedByName,
    DateTime CreatedAt,
    WriteUpAcknowledgment AcknowledgmentStatus,
    DateTime? AcknowledgmentAt,
    string? AcknowledgmentSignedName,
    // The drawn signature currently on the write-up (fetch the image from
    // .../signatures/{id}); null unless it is Acknowledged with a drawing.
    int? AcknowledgmentSignatureId,
    bool IsVoided,
    DateTime? VoidedAt,
    string? VoidReason,
    IReadOnlyList<WriteUpEventDto>? History);

// Severity and Type are optional so a client that leaves them out gets
// Normal / Written.
public record CreateWriteUpRequest(
    DateOnly Date,
    string Description,
    WriteUpSeverity? Severity = null,
    WriteUpType? Type = null);

public record UpdateWriteUpRequest(
    DateOnly Date,
    string Description,
    WriteUpSeverity Severity,
    WriteUpType Type);

public record VoidWriteUpRequest(string Reason);

// To confirm they received the write-up the employee types their full name
// (it has to match the name on their account, ignoring case and extra
// spaces) and draws a signature. Signature is a "data:image/png;base64,..."
// URL, as a canvas produces; see SignaturePng for what's accepted.
public record AcknowledgeWriteUpRequest(string TypedName, string Signature);
