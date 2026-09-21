using Server.Models;

namespace Server.Dtos;

public record WriteUpEventDto(
    int Id,
    WriteUpEventAction Action,
    int ByAccountId,
    string ByName,
    DateTime At,
    string? Detail);

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
