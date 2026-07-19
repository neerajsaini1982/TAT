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

// Lets a Lead/Admin set every punch on a shift's TimeEntry directly —
// correcting a mistake, or filling one in from scratch when the employee
// never clocked in at all. ClockInAt is required (a TimeEntry can't exist
// without one); everything else is optional, same shape as the entry
// itself. Note is required for the same audit reason as AdminClockOutRequest.
public record AdminEditTimeEntryRequest(
    DateTime ClockInAt,
    DateTime? BreakStartAt,
    DateTime? BreakEndAt,
    DateTime? LunchStartAt,
    DateTime? LunchEndAt,
    DateTime? Break2StartAt,
    DateTime? Break2EndAt,
    DateTime? ClockOutAt,
    string Note);
