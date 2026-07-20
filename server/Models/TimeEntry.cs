namespace Server.Models;

// A punch created when an employee clocks in for one of their scheduled
// shifts. One entry per ShiftAssignment (see AppDbContext's unique index).
// Any number of breaks/lunches, taken sequentially — at most one
// TimeEntrySegment open (EndAt null) at a time — see TimeEntriesController
// for the allowed state transitions.
public class TimeEntry
{
    public int Id { get; set; }

    public int AccountId { get; set; }
    public Account? Account { get; set; }

    public int ShiftAssignmentId { get; set; }
    public ShiftAssignment? ShiftAssignment { get; set; }

    public DateTime ClockInAt { get; set; }
    public DateTime? ClockOutAt { get; set; }

    public List<TimeEntrySegment> Segments { get; set; } = [];

    // Set when a Lead/Admin closes this entry out on the employee's behalf
    // (see TimeEntriesController.AdminClockOut) instead of the employee
    // clocking themselves out — e.g. they left early. Null for a normal
    // self clock-out. Note carries the reason and is required in that case.
    public int? ClockedOutByAccountId { get; set; }
    public Account? ClockedOutByAccount { get; set; }
    public string? Note { get; set; }

    // Who last used AdminEditTimes on this entry and when — kept for
    // reporting even though the affected punch fields themselves get
    // overwritten. Null for an entry that's only ever been self-punched.
    public int? EditedByAccountId { get; set; }
    public Account? EditedByAccount { get; set; }
    public DateTime? EditedAt { get; set; }
}
