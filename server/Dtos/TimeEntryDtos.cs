using Server.Models;

namespace Server.Dtos;

public record TimeEntrySegmentDto(int Id, BreakKind Kind, DateTime StartAt, DateTime? EndAt);

public record TimeEntryDto(
    int Id,
    int ShiftAssignmentId,
    int AccountId,
    DateTime ClockInAt,
    DateTime? ClockOutAt,
    List<TimeEntrySegmentDto> Segments,
    int? ClockedOutByAccountId,
    string? Note,
    int? EditedByAccountId,
    DateTime? EditedAt);

public record ClockInRequest(int ShiftAssignmentId);

public record StartSegmentRequest(BreakKind Kind);

// Note is required: the whole point of a Lead/Admin closing someone else's
// entry out is to record why (left early, no-show for the rest of the
// shift, etc).
public record AdminClockOutRequest(string Note);

public record AdminSegmentInput(BreakKind Kind, DateTime StartAt, DateTime? EndAt);

// Lets a Lead/Admin set every punch on a shift's TimeEntry directly —
// correcting a mistake, or filling one in from scratch when the employee
// never clocked in at all. ClockInAt is required (a TimeEntry can't exist
// without one); Segments wholesale-replaces whatever segments already
// exist on the entry. Note is required for the same audit reason as
// AdminClockOutRequest.
public record AdminEditTimeEntryRequest(
    DateTime ClockInAt,
    DateTime? ClockOutAt,
    List<AdminSegmentInput> Segments,
    string Note);
