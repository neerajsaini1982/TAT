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
    DateTime? ClockOutAt,
    int? ClockedOutByAccountId,
    string? Note);

public record ClockInRequest(int ShiftAssignmentId);

// Note is required: the whole point of a Lead/Admin closing someone else's
// entry out is to record why (left early, no-show for the rest of the
// shift, etc).
public record AdminClockOutRequest(string Note);
