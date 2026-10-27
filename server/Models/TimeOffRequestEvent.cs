namespace Server.Models;

public enum TimeOffEventAction
{
    Submitted,
    Approved,
    Denied,
    Cancelled,
    Edited,
}

// One row in a time-off request's history — append-only, same pattern as
// WriteUpEvent. Employees never see these; admins do.
public class TimeOffRequestEvent
{
    public int Id { get; set; }

    public int TimeOffRequestId { get; set; }
    public TimeOffRequest? TimeOffRequest { get; set; }

    public TimeOffEventAction Action { get; set; }

    public int ActorAccountId { get; set; }
    public Account? ActorAccount { get; set; }

    public DateTime At { get; set; } = DateTime.UtcNow;

    public string? Comment { get; set; }
}
