namespace Server.Dtos;

public record TimeEntryDto(
    int Id,
    int ShiftAssignmentId,
    int AccountId,
    DateTime ClockInAt,
    DateTime? ClockOutAt);

public record ClockInRequest(int ShiftAssignmentId);
