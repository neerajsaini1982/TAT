namespace Server.Dtos;

public record ShiftAssignmentDto(
    int Id,
    int ShiftId,
    string ShiftName,
    TimeOnly ShiftStartTime,
    TimeOnly ShiftEndTime,
    double Hours,
    int AccountId,
    string AccountFirstName,
    string AccountLastName,
    DateOnly Date,
    bool IsPublished);

public record CreateShiftAssignmentRequest(int ShiftId, int AccountId, DateOnly Date);

// Moves an existing assignment to a different employee and/or date, so a
// drag-and-drop reorder doesn't need to delete-then-recreate.
public record MoveShiftAssignmentRequest(int AccountId, DateOnly Date);

public record PublishScheduleRequest(string? LocationCode, DateOnly WeekStartDate);
