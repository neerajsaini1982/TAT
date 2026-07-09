namespace Server.Models;

public class Shift
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public bool IsBreakRequired { get; set; }
    public bool IsLunchRequired { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public int LocationId { get; set; }
    public Location? Location { get; set; }
}
