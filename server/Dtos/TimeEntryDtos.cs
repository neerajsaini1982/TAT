namespace Server.Dtos;

public record TimeEntryDto(
    int Id,
    int ShiftAssignmentId,
    int AccountId,
    DateTime ClockInAt,
    DateTime? BreakStartAt,
    DateTime? BreakEndAt,
    DateTime? LunchStartAt,
    DateTime? LunchEndAt,
    DateTime? Break2StartAt,
    DateTime? Break2EndAt,
    DateTime? ClockOutAt);

public record ClockInRequest(int ShiftAssignmentId);
