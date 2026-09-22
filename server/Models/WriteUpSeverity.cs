namespace Server.Models;

// Ordered least to most serious. Stored as its name (see AppDbContext), so
// reordering or inserting a level later doesn't change existing rows.
public enum WriteUpSeverity
{
    Low,
    Normal,
    High,
    Critical,
}
