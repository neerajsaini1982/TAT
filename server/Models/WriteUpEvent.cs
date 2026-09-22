namespace Server.Models;

// One row in a write-up's audit trail — append-only, never edited or
// removed. It is the only record of who voided, acknowledged or declined a
// write-up (WriteUp itself only keeps the resulting state), and of what a
// write-up said before it was edited.
public class WriteUpEvent
{
    public int Id { get; set; }

    public int WriteUpId { get; set; }
    public WriteUp? WriteUp { get; set; }

    public WriteUpEventAction Action { get; set; }

    public int ByAccountId { get; set; }
    public Account? ByAccount { get; set; }

    public DateTime At { get; set; } = DateTime.UtcNow;

    // Set on an Acknowledged event: the signature given at that moment.
    public int? SignatureId { get; set; }
    public WriteUpSignature? Signature { get; set; }

    // Human-readable specifics: for Edited, each changed field as
    // "Field: old → new" (so the previous wording is recoverable); for
    // Voided, the reason.
    public string? Detail { get; set; }
}
