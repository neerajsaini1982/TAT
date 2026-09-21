namespace Server.Models;

// The step of progressive discipline a write-up represents — separate from
// WriteUpSeverity, which is how serious the incident itself was. Stored as
// its name (see AppDbContext), so inserting a step later doesn't change
// existing rows.
public enum WriteUpType
{
    // Documented for the record, not a disciplinary step (coaching, a
    // conversation, an incident worth keeping a note of).
    Note,
    Verbal,
    Written,
    Final,
}
