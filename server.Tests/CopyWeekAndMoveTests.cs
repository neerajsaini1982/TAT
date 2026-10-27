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

// Admin fills a week from the one before, and moves shifts between employees.
public sealed class CopyWeekAndMoveTests : IDisposable
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

    public CopyWeekAndMoveTests()
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

    private static readonly DateOnly NextMonday = Monday.AddDays(7);

    private void DevelopmentMode(bool on)
    {
        var settings = db.LocationSettings.SingleOrDefault(s => s.LocationId == location.Id);
        if (settings is null)
        {
            db.LocationSettings.Add(new LocationSettings { LocationId = location.Id, DevelopmentMode = on });
        }
        else
        {
            settings.DevelopmentMode = on;
        }
        db.SaveChanges();
    }

    private void MakeAvailable(Account who, DateOnly date)
    {
        db.Availabilities.Add(new Availability
        {
            AccountId = who.Id, WeekStartDate = date, IsSubmitted = true,
            Days = [new AvailabilityDay { Date = date, IsAvailable = true }],
        });
        db.SaveChanges();
    }

    private async Task<CopyWeekResultDto> Copy(DateOnly? from = null, DateOnly? to = null, bool overwrite = false)
    {
        var result = await As(admin).CopyWeek(new CopyWeekRequest(location.LocationCode, from ?? Monday, to ?? NextMonday, overwrite));
        return Assert.IsType<CopyWeekResultDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
    }

    private List<ShiftAssignment> Week(DateOnly start) =>
        NewContext().ShiftAssignments.Where(a => a.Date >= start && a.Date <= start.AddDays(6)).ToList();

    [Fact]
    public async Task CopyWeek_CopiesEachShiftToTheSameWeekdayAsDrafts()
    {
        DevelopmentMode(true);
        Assign(morning, magaly);
        Assign(evening, adrian, Monday.AddDays(3));

        var result = await Copy();

        Assert.Equal(2, result.CopiedCount);
        Assert.Empty(result.Skipped);
        var copies = Week(NextMonday);
        Assert.Contains(copies, a => a.AccountId == magaly.Id && a.ShiftId == morning.Id && a.Date == NextMonday);
        Assert.Contains(copies, a => a.AccountId == adrian.Id && a.ShiftId == evening.Id && a.Date == NextMonday.AddDays(3));
        Assert.All(copies, a => Assert.False(a.IsPublished));
    }

    [Fact]
    public async Task CopyWeek_SkipsEmployeesAlreadyScheduledThatDay()
    {
        DevelopmentMode(true);
        Assign(morning, magaly);
        Assign(evening, magaly, NextMonday);

        var result = await Copy();

        Assert.Equal(0, result.CopiedCount);
        Assert.Equal("Already has a shift that day.", Assert.Single(result.Skipped).Reason);
        Assert.Equal(evening.Id, Assert.Single(Week(NextMonday)).ShiftId);
    }

    [Fact]
    public async Task CopyWeek_Overwrite_ReplacesTheWeeksShifts()
    {
        DevelopmentMode(true);
        Assign(morning, magaly);
        Assign(evening, magaly, NextMonday);
        Assign(morning, adrian, NextMonday.AddDays(2));

        var result = await Copy(overwrite: true);

        Assert.Equal(1, result.CopiedCount);
        Assert.Equal(2, result.RemovedCount);
        Assert.Empty(result.Skipped);
        var week = Week(NextMonday);
        var only = Assert.Single(week);
        Assert.Equal((magaly.Id, morning.Id, NextMonday), (only.AccountId, only.ShiftId, only.Date));
    }

    [Fact]
    public async Task CopyWeek_Overwrite_KeepsShiftsWithAttendance()
    {
        DevelopmentMode(true);
        Assign(morning, magaly);
        var absent = Assign(evening, magaly, NextMonday);
        absent.IsAbsent = true;
        db.SaveChanges();

        var result = await Copy(overwrite: true);

        Assert.Equal(0, result.CopiedCount);
        Assert.Equal(1, result.KeptCount);
        Assert.Equal("Already has a shift that day.", Assert.Single(result.Skipped).Reason);
        Assert.Equal(absent.Id, Assert.Single(Week(NextMonday)).Id);
    }

    [Fact]
    public async Task CopyWeek_SkipsInactiveEmployeesAndDeletedShifts()
    {
        DevelopmentMode(true);
        Assign(morning, magaly);
        Assign(evening, adrian);
        magaly.IsActive = false;
        evening.IsActive = false;
        db.SaveChanges();

        var result = await Copy();

        Assert.Equal(0, result.CopiedCount);
        Assert.Equal(2, result.Skipped.Count);
        Assert.Empty(Week(NextMonday));
    }

    [Fact]
    public async Task CopyWeek_ChecksAvailabilityAndTimeOffOutsideDevelopmentMode()
    {
        Assign(morning, magaly);
        Assign(evening, adrian);
        MakeAvailable(adrian, NextMonday);
        var type = new TimeOffType { LocationId = location.Id, Name = "Vacation" };
        db.TimeOffTypes.Add(type);
        db.SaveChanges();
        db.TimeOffRequests.Add(new TimeOffRequest
        {
            AccountId = adrian.Id, LocationId = location.Id, TimeOffTypeId = type.Id,
            StartDate = NextMonday, EndDate = NextMonday, Status = TimeOffStatus.Approved, CreatedByAccountId = admin.Id,
        });
        db.SaveChanges();

        var result = await Copy();

        Assert.Equal(0, result.CopiedCount);
        Assert.Equal("Not available that day.", result.Skipped.Single(s => s.AccountId == magaly.Id).Reason);
        Assert.Equal("Approved time off.", result.Skipped.Single(s => s.AccountId == adrian.Id).Reason);
    }

    [Fact]
    public async Task CopyWeek_LeavesOutCoverShifts()
    {
        DevelopmentMode(true);
        var absent = Assign(morning, magaly);
        var cover = Assign(morning, adrian);
        cover.CoversAssignmentId = absent.Id;
        db.SaveChanges();

        var result = await Copy();

        Assert.Equal(1, result.CopiedCount);
        Assert.Equal(magaly.Id, Assert.Single(Week(NextMonday)).AccountId);
    }

    [Fact]
    public async Task CopyWeek_RejectsCopyingAWeekOntoItself()
    {
        var result = await As(admin).CopyWeek(new CopyWeekRequest(location.LocationCode, Monday, Monday));
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Move_HandsTheShiftToAnotherEmployee()
    {
        DevelopmentMode(true);
        var m = Assign(morning, magaly);

        var result = await As(admin).Move(m.Id, new MoveShiftAssignmentRequest(adrian.Id, Monday));

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(adrian.Id, Reload(m.Id).AccountId);
    }

    [Fact]
    public async Task Move_IsBlockedForAbsentOrClockedInShifts()
    {
        DevelopmentMode(true);
        var absent = Assign(morning, magaly);
        absent.IsAbsent = true;
        db.SaveChanges();

        var result = await As(admin).Move(absent.Id, new MoveShiftAssignmentRequest(adrian.Id, Monday));

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal(magaly.Id, Reload(absent.Id).AccountId);
    }

    [Fact]
    public async Task Move_IsBlockedWhenTheOtherEmployeeIsAlreadyWorkingThatDay()
    {
        DevelopmentMode(true);
        var m = Assign(morning, magaly);
        Assign(evening, adrian);

        var result = await As(admin).Move(m.Id, new MoveShiftAssignmentRequest(adrian.Id, Monday));

        Assert.IsType<ConflictObjectResult>(result.Result);
    }
}
