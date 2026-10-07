namespace Server.Models;

// One employee assigned to work a shift template on a specific date.
public class ShiftAssignment
{
    public int Id { get; set; }

    public int ShiftId { get; set; }
    public Shift? Shift { get; set; }

    public int AccountId { get; set; }
    public Account? Account { get; set; }

    public DateOnly Date { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Draft until the admin clicks Post for this week; employees never see
    // an assignment (via GetMine) until it's published.
    public bool IsPublished { get; set; }
    public DateTime? PublishedAt { get; set; }

    // Set by a Lead/Admin when the employee didn't show up (see
    // ShiftAssignmentsController.MarkAbsent). Only valid while no TimeEntry
    // exists yet for this assignment; clocking in clears both fields since
    // the employee showing up supersedes an earlier absence mark.
    public bool IsAbsent { get; set; }
    public string? AbsenceNote { get; set; }

    // Who clicked Mark/Clear Absent and when — kept independent of IsAbsent
    // itself (which flips back to false on clear) so there's always a
    // record of the most recent action here for reporting.
    public int? AbsentMarkedByAccountId { get; set; }
    public Account? AbsentMarkedByAccount { get; set; }
    public DateTime? AbsentMarkedAt { get; set; }

    // Manually entered by an admin (see ShiftAssignmentsController.SetSickMinutes)
    // when the employee reported in sick for this shift — there's no
    // clock-in to derive it from, unlike worked time. Counted as paid time
    // in the hours report (see ReportsController), separate from IsAbsent
    // since an admin may want to record hours here without also marking the
    // day absent, or vice versa.
    public int SickMinutes { get; set; }
    public int? SickHoursRecordedByAccountId { get; set; }
    public Account? SickHoursRecordedByAccount { get; set; }
    public DateTime? SickHoursRecordedAt { get; set; }

    // Set on a cover assignment: the absent assignment this one stands in
    // for — see ShiftAssignmentsController.CallOut. The absent assignment
    // itself is left on its own employee, so the call-out stays on record.
    // Null for an ordinary shift, and nulled if the absent assignment is
    // deleted.
    public int? CoversAssignmentId { get; set; }
    public ShiftAssignment? CoversAssignment { get; set; }

    // The other end of CoversAssignment — the assignment covering this one,
    // if any. At most one.
    public ShiftAssignment? CoveredByAssignment { get; set; }

    public int? CoverAssignedByAccountId { get; set; }
    public Account? CoverAssignedByAccount { get; set; }
    public DateTime? CoverAssignedAt { get; set; }

    // Set when the cover employee was already working that day: rather than
    // a second assignment, their own one was switched to a single combined
    // shift spanning both (so ShiftId now points at that), and this is the
    // shift they had before — what they go back to if the cover is removed.
    // Null on a cover assignment created for someone who had the day off.
    public int? OriginalShiftId { get; set; }
    public Shift? OriginalShift { get; set; }
}
