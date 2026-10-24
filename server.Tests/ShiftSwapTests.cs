using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Server.Controllers;
using Server.Data;
using Server.Dtos;
using Server.Hubs;
using Server.Models;
using Server.Security;

namespace Server.Tests;

// Admin swaps the shifts of two employees working the same day.
public sealed class ShiftSwapTests : IDisposable
{
    private static readonly DateOnly Monday = new(2026, 9, 14);

    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly DbContextOptions<AppDbContext> options;
    private readonly List<AppDbContext> contexts = [];
    private readonly AppDbContext db;

    private readonly Location location;
    private readonly Shift morning;
    private readonly Shift evening;
    private readonly Account admin;
    private readonly Account magaly;
    private readonly Account adrian;

    public ShiftSwapTests()
    {
        connection.Open();
        options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        db = NewContext();
        db.Database.EnsureCreated();

        location = new Location { Name = "Test", LocationCode = "t1" };
        db.Locations.Add(location);
        db.SaveChanges();

        morning = SeedShift("Morning", 8, 16);
        evening = SeedShift("Evening", 12, 20);
        admin = Seed("admin", AccountRole.Admin);
        magaly = Seed("magaly", AccountRole.Employee);
        adrian = Seed("adrian", AccountRole.Employee);
    }

    public void Dispose()
    {
        foreach (var context in contexts)
        {
            context.Dispose();
        }
        connection.Dispose();
    }

    private AppDbContext NewContext()
    {
        var context = new AppDbContext(options);
        contexts.Add(context);
        return context;
    }

    private Shift SeedShift(string name, int startHour, int endHour)
    {
        var shift = new Shift
        {
            Name = name,
            StartTime = new TimeOnly(startHour, 0),
            EndTime = new TimeOnly(endHour, 0),
            LocationId = location.Id,
        };
        db.Shifts.Add(shift);
        db.SaveChanges();
        return shift;
    }

    private Account Seed(string username, AccountRole role)
    {
        var account = new Account
        {
            Username = username,
            FirstName = username,
            LastName = "Tester",
            Role = role,
            LocationId = location.Id,
            IsActive = true,
        };
        db.Accounts.Add(account);
        db.SaveChanges();
        return account;
    }

    private ShiftAssignment Assign(Shift shift, Account account, DateOnly? date = null)
    {
        var assignment = new ShiftAssignment { ShiftId = shift.Id, AccountId = account.Id, Date = date ?? Monday, IsPublished = true };
        db.ShiftAssignments.Add(assignment);
        db.SaveChanges();
        return assignment;
    }

    private ShiftAssignmentsController As(Account caller)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, caller.Id.ToString()),
            new(ClaimTypes.Role, caller.Role.ToString()),
            new(TokenService.LocationCodeClaimType, location.LocationCode),
        };
        return new ShiftAssignmentsController(NewContext(), new SilentNotifier(), new RecordingEmailSender())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) },
            },
        };
    }

    private sealed class SilentNotifier : IScheduleNotifier
    {
        public Task NotifyLocationChanged(string locationCode) => Task.CompletedTask;
    }

    private ShiftAssignment Reload(int id) => NewContext().ShiftAssignments.Single(a => a.Id == id);

    [Fact]
    public async Task Swap_ExchangesShiftsAndKeepsEachEmployeeOnTheirOwnAssignment()
    {
        var m = Assign(morning, magaly);
        var a = Assign(evening, adrian);

        var result = await As(admin).Swap(new SwapShiftAssignmentsRequest(m.Id, a.Id));

        var dtos = Assert.IsAssignableFrom<IEnumerable<ShiftAssignmentDto>>(Assert.IsType<OkObjectResult>(result.Result).Value).ToList();
        Assert.Equal(evening.Id, dtos.Single(d => d.AccountId == magaly.Id).ShiftId);
        Assert.Equal(morning.Id, dtos.Single(d => d.AccountId == adrian.Id).ShiftId);

        Assert.Equal((magaly.Id, evening.Id), (Reload(m.Id).AccountId, Reload(m.Id).ShiftId));
        Assert.Equal((adrian.Id, morning.Id), (Reload(a.Id).AccountId, Reload(a.Id).ShiftId));
    }

    [Fact]
    public async Task Swap_RejectsDifferentDays()
    {
        var m = Assign(morning, magaly);
        var a = Assign(evening, adrian, Monday.AddDays(1));

        var result = await As(admin).Swap(new SwapShiftAssignmentsRequest(m.Id, a.Id));

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal(morning.Id, Reload(m.Id).ShiftId);
    }

    [Fact]
    public async Task Swap_RejectsOnceSomeoneHasClockedIn()
    {
        var m = Assign(morning, magaly);
        var a = Assign(evening, adrian);
        db.TimeEntries.Add(new TimeEntry { AccountId = magaly.Id, ShiftAssignmentId = m.Id, ClockInAt = DateTime.UtcNow });
        db.SaveChanges();

        var result = await As(admin).Swap(new SwapShiftAssignmentsRequest(m.Id, a.Id));

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal(morning.Id, Reload(m.Id).ShiftId);
    }

    [Fact]
    public async Task Swap_RejectsAbsentEmployee()
    {
        var m = Assign(morning, magaly);
        var a = Assign(evening, adrian);
        db.ShiftAssignments.Find(a.Id)!.IsAbsent = true;
        db.SaveChanges();

        var result = await As(admin).Swap(new SwapShiftAssignmentsRequest(m.Id, a.Id));

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }
}
