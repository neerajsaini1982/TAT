using Server.Models;

namespace Server.Dtos;

public record KioskClockInRequest(int AccountId, int ShiftAssignmentId, string Pin);

public record KioskStartSegmentRequest(int AccountId, string Pin, BreakKind Kind);

public record KioskPinRequest(int AccountId, string Pin);
