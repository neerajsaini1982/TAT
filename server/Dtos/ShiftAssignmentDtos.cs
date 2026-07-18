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
    bool IsPublished,
    bool IsAbsent,
    string? AbsenceNote);

public record CreateShiftAssignmentRequest(int ShiftId, int AccountId, DateOnly Date);

// Moves an existing assignment to a different employee and/or date, so a
// drag-and-drop reorder doesn't need to delete-then-recreate.
public record MoveShiftAssignmentRequest(int AccountId, DateOnly Date);

// Note is required when marking absent (explains why); optional/ignored
// when clearing it.
public record MarkAbsentRequest(bool IsAbsent, string? Note);

public record PublishScheduleRequest(string? LocationCode, DateOnly WeekStartDate);

// Shown on the (unauthenticated) employee login screen, so it can't expose
// anything beyond first name + last initial. IsClockedIn/IsClockedOut are
// the only punch details surfaced here — no timestamps, no notes.
public record TodayScheduleEntryDto(
    string ShiftName,
    TimeOnly ShiftStartTime,
    TimeOnly ShiftEndTime,
    string EmployeeName,
    bool IsClockedIn,
    bool IsClockedOut);
