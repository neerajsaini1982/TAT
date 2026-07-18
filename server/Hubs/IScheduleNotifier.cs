using Microsoft.AspNetCore.SignalR;

namespace Server.Hubs;

// Broadcasts a "go refetch" signal to every client watching a location, so
// posted schedule/attendance changes show up without a manual reload.
public interface IScheduleNotifier
{
    Task NotifyLocationChanged(string locationCode);
}

public class ScheduleNotifier(IHubContext<ScheduleHub> hubContext) : IScheduleNotifier
{
    public Task NotifyLocationChanged(string locationCode) =>
        hubContext.Clients.Group(ScheduleHub.GroupName(locationCode)).SendAsync("scheduleChanged");
}
