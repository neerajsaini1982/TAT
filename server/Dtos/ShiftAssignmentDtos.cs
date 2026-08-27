namespace Server.Dtos;

public record ShiftAssignmentDto(
    int Id,
    int ShiftId,
    string ShiftName,
    TimeOnly ShiftStartTime,
    TimeOnly ShiftEndTime,
    List<ScheduledBreakDto> ScheduledBreaks,
    double Hours,
    int AccountId,
    string AccountFirstName,
    string AccountLastName,
    DateOnly Date,
    bool IsPublished,
    bool IsAbsent,
    string? AbsenceNote,
    int? AbsentMarkedByAccountId,
    DateTime? AbsentMarkedAt,
    int SickMinutes,
    int? SickHoursRecordedByAccountId,
    DateTime? SickHoursRecordedAt);

public record CreateShiftAssignmentRequest(int ShiftId, int AccountId, DateOnly Date);

// Moves an existing assignment to a different employee and/or date, so a
// drag-and-drop reorder doesn't need to delete-then-recreate.
public record MoveShiftAssignmentRequest(int AccountId, DateOnly Date);

// Note is required when marking absent (explains why); optional/ignored
// when clearing it.
public record MarkAbsentRequest(bool IsAbsent, string? Note);

// Overwrites the assignment's sick minutes outright (not an increment) —
// the admin is transcribing what the employee texted in, so re-entering it
// (e.g. to correct a typo) should just replace the prior value.
public record SetSickMinutesRequest(int SickMinutes);

public record PublishScheduleRequest(string? LocationCode, DateOnly WeekStartDate, bool SendEmail = false);
