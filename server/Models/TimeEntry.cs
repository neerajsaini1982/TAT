namespace Server.Models;

// A punch created when an employee clocks in for one of their scheduled
// shifts. One entry per ShiftAssignment (see AppDbContext's unique index).
// At most one break and one lunch per shift, taken sequentially — see
// TimeEntriesController for the allowed state transitions.
public class TimeEntry
{
    public int Id { get; set; }

    public int AccountId { get; set; }
    public Account? Account { get; set; }

    public int ShiftAssignmentId { get; set; }
    public ShiftAssignment? ShiftAssignment { get; set; }

    public DateTime ClockInAt { get; set; }
    public DateTime? BreakStartAt { get; set; }
    public DateTime? BreakEndAt { get; set; }
    public DateTime? LunchStartAt { get; set; }
    public DateTime? LunchEndAt { get; set; }
    public DateTime? ClockOutAt { get; set; }
}
