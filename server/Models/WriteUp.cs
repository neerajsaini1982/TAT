namespace Server.Models;

// A write-up / warning an admin records against an employee for tracking
// purposes. The employee can see their own (never anyone else's) but can't
// change it — they can only acknowledge it. A write-up is never deleted: a
// mistaken or rescinded one is voided (with a reason) so the record stays
// trustworthy, and every change is logged in Events. See WriteUpsController.
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

    public WriteUpType Type { get; set; } = WriteUpType.Written;

    public int CreatedByAccountId { get; set; }
    public Account? CreatedByAccount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public WriteUpAcknowledgment AcknowledgmentStatus { get; set; } = WriteUpAcknowledgment.Pending;

    // When the employee acknowledged, or the admin recorded a refusal; null
    // while Pending.
    public DateTime? AcknowledgmentAt { get; set; }

    // Set when voided; the reason is required. Voided write-ups stay in the
    // employee's list (marked as voided) but can no longer be edited or
    // acknowledged.
    public DateTime? VoidedAt { get; set; }
    public string? VoidReason { get; set; }
    public bool IsVoided => VoidedAt is not null;

    public List<WriteUpEvent> Events { get; set; } = [];
}
