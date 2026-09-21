using Server.Models;

namespace Server.Dtos;

public record WriteUpDto(
    int Id,
    int AccountId,
    DateOnly Date,
    string Description,
    WriteUpSeverity Severity,
    int CreatedByAccountId,
    string CreatedByName,
    DateTime CreatedAt);

// Severity is optional so a client that leaves it out gets Normal.
public record CreateWriteUpRequest(
    DateOnly Date,
    string Description,
    WriteUpSeverity? Severity = null);

public record UpdateWriteUpRequest(
    DateOnly Date,
    string Description,
    WriteUpSeverity Severity);
