namespace Server.Models;

// A scheduled break/lunch window on a Shift template — what the admin
// expects, not what actually happens (see TimeEntrySegment for that). A
// shift can have any number of these, of either Kind, e.g. two breaks and
// one lunch on a long shift.
public class ScheduledBreak
{
    public int Id { get; set; }

    public int ShiftId { get; set; }
    public Shift? Shift { get; set; }

    public BreakKind Kind { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
}
