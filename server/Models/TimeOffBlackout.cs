namespace Server.Models;

// Days a location doesn't allow time-off requests (e.g. "Thanksgiving
// week"). Only blocks employees asking; an admin can still enter time off
// on these days on someone's behalf.
public class TimeOffBlackout
{
    public int Id { get; set; }

    public int LocationId { get; set; }
    public Location? Location { get; set; }

    public string Name { get; set; } = string.Empty;

    // Inclusive range.
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
}
