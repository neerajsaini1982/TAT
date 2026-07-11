namespace Server.Models;

// A punch created when an employee clocks in for one of their scheduled
// shifts. One entry per ShiftAssignment (see AppDbContext's unique index).
// ClockOutAt is nullable because clocking out isn't implemented yet — every
// entry is currently open-ended.
public class TimeEntry
{
    public int Id { get; set; }

    public int AccountId { get; set; }
    public Account? Account { get; set; }

    public int ShiftAssignmentId { get; set; }
    public ShiftAssignment? ShiftAssignment { get; set; }

    public DateTime ClockInAt { get; set; }
    public DateTime? ClockOutAt { get; set; }
}
