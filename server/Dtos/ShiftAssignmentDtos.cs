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
    DateTime? SickHoursRecordedAt,
    // Set on a cover assignment: whose absent shift it stands in for.
    int? CoversAssignmentId,
    string? CoversAccountFirstName,
    string? CoversAccountLastName,
    // Set on an absent assignment that has cover: who is covering it.
    int? CoveredByAssignmentId,
    string? CoveredByAccountFirstName,
    string? CoveredByAccountLastName,
    // Set on a cover assignment whose employee was already working that
    // day: the shift they had before it was combined with the one they're
    // covering (the Shift* fields above describe the combined shift).
    string? OriginalShiftName);

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

// Marks the assignment absent and, when CoverAccountId is given, puts that
// employee on the shift — saved together or not at all. SickMinutes is
// admin-only (as SetSickMinutes is); null leaves any recorded value alone.
// ConfirmUnavailable/ConfirmCombine acknowledge the two warnings the cover
// picker shows: the cover employee said they weren't available that day, or
// already has a shift on it, which gets replaced. CoverShiftId picks what
// replaces it — one of the location's own shifts — and null means a
// combined shift spanning their shift and the absent one.
public record CallOutRequest(
    string? Note,
    int? SickMinutes,
    int? CoverAccountId,
    bool ConfirmUnavailable = false,
    bool ConfirmCombine = false,
    bool SendEmail = false,
    int? CoverShiftId = null);

// Assigns or replaces the cover on an assignment that's already absent.
public record AssignCoverRequest(
    int CoverAccountId,
    bool ConfirmUnavailable = false,
    bool ConfirmCombine = false,
    bool SendEmail = false,
    int? CoverShiftId = null);

// Both sides of a call-out, so the client can update the schedule without a
// refetch. Cover is null when none was assigned. EmailSent is false both
// when no email was asked for and when sending failed — the cover
// assignment stands either way.
public record CallOutResultDto(ShiftAssignmentDto Absent, ShiftAssignmentDto? Cover, bool EmailSent);

// A shift as the cover picker shows it: the one a candidate already has
// that day, or the combined one they'd be given.
public record CoverShiftPreviewDto(
    string ShiftName,
    TimeOnly StartTime,
    TimeOnly EndTime,
    List<ScheduledBreakDto> ScheduledBreaks,
    double Hours);

// One of the location's shifts that can be picked as the replacement.
public record CoverShiftOptionDto(int Id, string Name, TimeOnly StartTime, TimeOnly EndTime);

// One employee who could cover a shift. OwnShift is set when they already
// work that day; CombinedShift is then the single shift that would replace
// it — the one asked for with ?shiftId=, or by default their own and the
// one being covered end to end, in which case BridgedGapMinutes is any time
// between the two that it takes in. SuggestedShiftId is one of the
// location's own shifts with exactly that default span, if there is one.
// WeekScheduledHours is their scheduled hours for the workweek with the
// cover applied; OvertimeMinutes is how much of that would be paid at a
// premium under the location's overtime rules. BlockedReason is set when
// they can't be picked at all.
public record CoverCandidateDto(
    int AccountId,
    string FirstName,
    string LastName,
    bool IsAvailable,
    bool HasEmail,
    double WeekScheduledHours,
    int OvertimeMinutes,
    CoverShiftPreviewDto? OwnShift,
    CoverShiftPreviewDto? CombinedShift,
    int BridgedGapMinutes,
    int? SuggestedShiftId,
    string? BlockedReason);

// CurrentCoverAccountId is who covers the shift now, if anyone, and
// CurrentCoverShiftId the location shift they were moved onto for it (null
// for an automatically combined one, or when they had the day off). Shifts
// are the location's active shifts, for picking a replacement.
public record CoverCandidatesDto(
    bool EmailConfigured,
    int? CurrentCoverAccountId,
    int? CurrentCoverShiftId,
    List<CoverShiftOptionDto> Shifts,
    List<CoverCandidateDto> Candidates);

public record PublishScheduleRequest(string? LocationCode, DateOnly WeekStartDate, bool SendEmail = false);
