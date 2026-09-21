namespace Server.Models;

// A write-up / warning an admin records against an employee for tracking
// purposes. The employee can see their own (never anyone else's) but can't
// change or remove them — see WriteUpsController.
public class WriteUp
{
    public int Id { get; set; }

    public int AccountId { get; set; }
    public Account? Account { get; set; }

    // The day the incident/warning happened, as chosen by the admin — not
    // when the row was entered (that's CreatedAt), since write-ups are often
    // recorded after the fact.
    public DateOnly Date { get; set; }

    public string Description { get; set; } = string.Empty;

    public WriteUpSeverity Severity { get; set; } = WriteUpSeverity.Normal;

    public int CreatedByAccountId { get; set; }
    public Account? CreatedByAccount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
