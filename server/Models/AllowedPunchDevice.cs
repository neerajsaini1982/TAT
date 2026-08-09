namespace Server.Models;

// One IP address an Admin has approved for self-service punches at a
// location, used only when LocationSettings.ClockInAnywhere is off — see
// TimeEntriesController.
public class AllowedPunchDevice
{
    public int Id { get; set; }

    public int LocationId { get; set; }
    public Location? Location { get; set; }

    public string IpAddress { get; set; } = "";
    public string Label { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
