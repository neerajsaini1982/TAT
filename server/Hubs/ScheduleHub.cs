using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Server.Hubs;

// Pure "something changed, go refetch" signal for schedule/attendance data —
// never carries the data itself, so it stays anonymous while the real data
// stays behind the existing authenticated REST endpoints (see
// IScheduleNotifier). Clients join a per-location group so a change at one
// location doesn't wake up every other location's screens.
[AllowAnonymous]
public class ScheduleHub : Hub
{
    public Task JoinLocation(string locationCode) =>
        Groups.AddToGroupAsync(Context.ConnectionId, GroupName(locationCode));

    public static string GroupName(string locationCode) => $"location:{locationCode}";
}
