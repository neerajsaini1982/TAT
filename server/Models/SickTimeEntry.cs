namespace Server.Models;

// Sick hours an admin records by hand for a day the employee had no
// ShiftAssignment at all (e.g. calling in sick on a day off) — see
// ShiftAssignment.SickMinutes for the already-scheduled case, which this is
// deliberately kept separate from: there's no shift/availability to attach
// it to. ReportsController folds both sources into the same per-day sick
// total.
public class SickTimeEntry
{
    public int Id { get; set; }

    public int AccountId { get; set; }
    public Account? Account { get; set; }

    public DateOnly Date { get; set; }
    public int Minutes { get; set; }
    public string? Note { get; set; }

    public int RecordedByAccountId { get; set; }
    public Account? RecordedByAccount { get; set; }
    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;
}
