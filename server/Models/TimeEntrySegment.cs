namespace Server.Models;

// One actual break/lunch punch within a TimeEntry. Any number of these per
// entry (see BreakKind), but at most one open (EndAt null) at a time —
// enforced in TimeEntriesController, not here.
public class TimeEntrySegment
{
    public int Id { get; set; }

    public int TimeEntryId { get; set; }
    public TimeEntry? TimeEntry { get; set; }

    public BreakKind Kind { get; set; }
    public DateTime StartAt { get; set; }
    public DateTime? EndAt { get; set; }
}
