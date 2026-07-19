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
}
