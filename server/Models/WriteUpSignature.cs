namespace Server.Models;

// The drawn signature an employee gave when acknowledging a write-up, kept
// as the PNG image itself. Append-only, like WriteUpEvent: a later edit that
// resets the acknowledgment doesn't touch it, so the signature that was
// actually given stays on record (the "Acknowledged" WriteUpEvent points at
// it), and a re-acknowledgment adds a new row rather than replacing this one.
public class WriteUpSignature
{
    public int Id { get; set; }

    public int WriteUpId { get; set; }
    public WriteUp? WriteUp { get; set; }

    // What they typed alongside the drawing (their name on the account).
    public string SignedName { get; set; } = string.Empty;

    public DateTime SignedAt { get; set; } = DateTime.UtcNow;

    // PNG, validated by SignaturePng before it gets here. Kept out of the
    // normal write-up queries and served by its own endpoint — the list
    // response only carries the signature's id.
    public byte[] Png { get; set; } = [];
}
