namespace Server.Models;

// One employee's availability submission for a single week. Once
// IsSubmitted is true, the employee can no longer change it.
public class Availability
{
    public int Id { get; set; }
    public int AccountId { get; set; }
    public Account? Account { get; set; }

    // Monday of the week this availability covers.
    public DateOnly WeekStartDate { get; set; }

    public bool IsSubmitted { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<AvailabilityDay> Days { get; set; } = new List<AvailabilityDay>();
}
