namespace Server.Models;

// One employee assigned to work a shift template on a specific date.
public class ShiftAssignment
{
    public int Id { get; set; }

    public int ShiftId { get; set; }
    public Shift? Shift { get; set; }

    public int AccountId { get; set; }
    public Account? Account { get; set; }

    public DateOnly Date { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Draft until the admin clicks Post for this week; employees never see
    // an assignment (via GetMine) until it's published.
    public bool IsPublished { get; set; }
    public DateTime? PublishedAt { get; set; }
}
