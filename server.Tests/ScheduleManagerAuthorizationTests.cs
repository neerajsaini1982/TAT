using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Server.Data;
using Server.Models;
using Server.Security;

namespace Server.Tests;

// The "ScheduleManager" policy guarding Mark Absent, cover and punch edits:
// Lead and above always pass; an Employee passes only with
// Account.CanManageOthersSchedules — Can See Others' Schedules alone is not
// enough.
public sealed class ScheduleManagerAuthorizationTests : IDisposable
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly AppDbContext db;
    private readonly Location location;

    public ScheduleManagerAuthorizationTests()
    {
        connection.Open();
        db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        db.Database.EnsureCreated();

        location = new Location { Name = "Test", LocationCode = "t1" };
        db.Locations.Add(location);
        db.SaveChanges();
    }

    public void Dispose()
    {
        db.Dispose();
        connection.Dispose();
    }

    private Account Seed(AccountRole role, bool canManage = false, bool canSee = false, bool active = true)
    {
        var account = new Account
        {
            Username = $"user{db.Accounts.Count()}",
            Role = role,
            LocationId = location.Id,
            IsActive = active,
            CanManageOthersSchedules = canManage,
            CanSeeAllSchedules = canSee,
        };
        db.Accounts.Add(account);
        db.SaveChanges();
        return account;
    }

    private async Task<bool> Allowed(Account account)
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, account.Id.ToString()),
                new Claim(ClaimTypes.Role, account.Role.ToString()),
            ],
            "Test"));
        var requirement = new ScheduleManagerRequirement();
        var context = new AuthorizationHandlerContext([requirement], user, null);
        await new ScheduleManagerHandler(db).HandleAsync(context);
        return context.HasSucceeded;
    }

    [Theory]
    [InlineData(AccountRole.Sa)]
    [InlineData(AccountRole.Admin)]
    [InlineData(AccountRole.Lead)]
    public async Task LeadAndAboveAlwaysPass(AccountRole role) =>
        Assert.True(await Allowed(Seed(role)));

    [Fact]
    public async Task PlainEmployeeIsRefused() =>
        Assert.False(await Allowed(Seed(AccountRole.Employee)));

    [Fact]
    public async Task SeeingOthersSchedulesAloneIsNotEnough() =>
        Assert.False(await Allowed(Seed(AccountRole.Employee, canSee: true)));

    [Fact]
    public async Task EmployeeWithManageAccessPasses() =>
        Assert.True(await Allowed(Seed(AccountRole.Employee, canManage: true)));

    [Fact]
    public async Task InactiveEmployeeWithManageAccessIsRefused() =>
        Assert.False(await Allowed(Seed(AccountRole.Employee, canManage: true, active: false)));

    [Fact]
    public async Task KioskIsRefusedEvenIfFlagged() =>
        Assert.False(await Allowed(Seed(AccountRole.Kiosk, canManage: true)));
}
