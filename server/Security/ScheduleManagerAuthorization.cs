using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Server.Data;
using Server.Models;

namespace Server.Security;

// The "ScheduleManager" policy: who can act on someone else's shift from the
// schedule screen — mark them absent, find cover, fix their punch times.
// Sa/Admin/Lead always can (same as LeadOrAbove). An Employee can only if an
// admin turned on Account.CanManageOthersSchedules for them. Read from the
// database rather than a token claim so turning the toggle off takes effect
// on the very next request, not whenever their token next expires.
public class ScheduleManagerRequirement : IAuthorizationRequirement;

public class ScheduleManagerHandler(AppDbContext db) : AuthorizationHandler<ScheduleManagerRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, ScheduleManagerRequirement requirement)
    {
        var user = context.User;
        if (user.IsInRole(nameof(AccountRole.Sa)) ||
            user.IsInRole(nameof(AccountRole.Admin)) ||
            user.IsInRole(nameof(AccountRole.Lead)))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        if (user.IsInRole(nameof(AccountRole.Employee)) &&
            int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var accountId) &&
            db.Accounts.Any(a => a.Id == accountId && a.IsActive && a.CanManageOthersSchedules))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
