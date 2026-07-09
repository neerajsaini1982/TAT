namespace Server.Models;

public class AvailabilityDay
{
    public int Id { get; set; }
    public int AvailabilityId { get; set; }
    public Availability? Availability { get; set; }

    public DateOnly Date { get; set; }
    public bool IsAvailable { get; set; }

    // Null when not available, or when available for the entire day
    // ("open availability" in the old WhatsApp messages). Both set when
    // available for a specific window only (e.g. "3:00-close").
    public TimeOnly? StartTime { get; set; }
    public TimeOnly? EndTime { get; set; }
}
