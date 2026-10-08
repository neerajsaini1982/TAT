using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Server.Controllers;
using Server.Data;
using Server.Dtos;
using Server.Models;
using Server.Security;

namespace Server.Tests;

// ReportsController.GetCallOutReport: per employee, how often they were
// marked absent and how often they covered for someone else.
public sealed class CallOutReportTests : IDisposable
{
    // 2026-09-14 is a Monday.
    private static readonly DateOnly Monday = new(2026, 9, 14);

    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly AppDbContext db;
    private readonly Location location;
    private readonly Location otherLocation;
    private readonly Shift morning;
    private readonly Shift evening;
    private readonly Account admin;
    private readonly Account sam;
    private readonly Account alex;
    private readonly Account jo;

    public CallOutReportTests()
    {
        connection.Open();
        db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        db.Database.EnsureCreated();

        location = new Location { Name = "Test", LocationCode = "t1" };
        otherLocation = new Location { Name = "Other", LocationCode = "t2" };
        db.Locations.AddRange(location, otherLocation);
        db.SaveChanges();

        morning = new Shift { Name = "Morning", StartTime = new TimeOnly(7, 0), EndTime = new TimeOnly(15, 0), LocationId = location.Id };
        evening = new Shift { Name = "Evening", StartTime = new TimeOnly(15, 0), EndTime = new TimeOnly(23, 0), LocationId = location.Id };
        db.Shifts.AddRange(morning, evening);
        db.SaveChanges();

        admin = Seed("admin", AccountRole.Admin, location);
        sam = Seed("sam", AccountRole.Employee, location);
        alex = Seed("alex", AccountRole.Employee, location);
        jo = Seed("jo", AccountRole.Employee, location);
    }

    public void Dispose()
    {
        db.Dispose();
        connection.Dispose();
    }

    private Account Seed(string username, AccountRole role, Location at)
    {
        var account = new Account { Username = username, FirstName = username, LastName = "Tester", Role = role, LocationId = at.Id };
        db.Accounts.Add(account);
        db.SaveChanges();
        return account;
    }

    private ShiftAssignment Assign(Shift shift, Account account, DateOnly date, Action<ShiftAssignment>? configure = null)
    {
        var assignment = new ShiftAssignment { ShiftId = shift.Id, AccountId = account.Id, Date = date, IsPublished = true };
        configure?.Invoke(assignment);
        db.ShiftAssignments.Add(assignment);
        db.SaveChanges();
        return assignment;
    }

    private ShiftAssignment CallOut(Shift shift, Account account, DateOnly date, string note = "Sick") =>
        Assign(shift, account, date, a =>
        {
            a.IsAbsent = true;
            a.AbsenceNote = note;
        });

    private ReportsController As(Account caller) => new(db, new RecordingEmailSender())
    {
        ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, caller.Id.ToString()),
                    new Claim(ClaimTypes.Role, caller.Role.ToString()),
                    new Claim(TokenService.LocationCodeClaimType, db.Locations.Find(caller.LocationId)!.LocationCode),
                ], "test")),
            },
        },
    };

    private Dictionary<string, EmployeeCallOutReportDto> Run(DateOnly start, DateOnly end) =>
        Assert.IsAssignableFrom<IEnumerable<EmployeeCallOutReportDto>>(
            Assert.IsType<OkObjectResult>(As(admin).GetCallOutReport(null, start, end).Result).Value)
            .ToDictionary(r => r.FullName);

    [Fact]
    public void It_counts_call_outs_and_covers_per_employee_with_the_details_behind_them()
    {
        // Monday: sam calls out, alex (day off) covers.
        var mondayAbsence = CallOut(evening, sam, Monday, "Flu");
        Assign(evening, alex, Monday, a => a.CoversAssignmentId = mondayAbsence.Id);
        // Tuesday: sam calls out again, nobody covers. jo just works.
        CallOut(evening, sam, Monday.AddDays(1), "Car trouble");
        Assign(morning, jo, Monday.AddDays(1));
        // Wednesday: jo calls out; alex, already on the morning shift, stays on.
        var wednesdayAbsence = CallOut(evening, jo, Monday.AddDays(2));
        var combined = new Shift
        {
            Name = "Morning + Evening", StartTime = new TimeOnly(7, 0), EndTime = new TimeOnly(23, 0),
            LocationId = location.Id, IsActive = false,
        };
        db.Shifts.Add(combined);
        db.SaveChanges();
        Assign(combined, alex, Monday.AddDays(2), a =>
        {
            a.CoversAssignmentId = wednesdayAbsence.Id;
            a.OriginalShiftId = morning.Id;
        });
        // Thursday: sam works a normal shift.
        Assign(morning, sam, Monday.AddDays(3));

        var rows = Run(Monday, Monday.AddDays(6));

        Assert.Equal(["alex Tester", "jo Tester", "sam Tester"], rows.Keys.Order().ToList());

        var samRow = rows["sam Tester"];
        Assert.Equal(3, samRow.ScheduledShifts);
        Assert.Equal(2, samRow.CallOuts);
        Assert.Equal(1, samRow.CallOutsCovered);
        Assert.Equal(0, samRow.ShiftsCovered);
        Assert.Equal(
            [(Monday, "Flu", "alex Tester"), (Monday.AddDays(1), "Car trouble", null)],
            samRow.CallOutDetails.Select(d => (d.Date, d.Note!, d.CoveredByName)).ToList());
        Assert.Equal("Evening", samRow.CallOutDetails[0].ShiftName);

        var alexRow = rows["alex Tester"];
        Assert.Equal(2, alexRow.ScheduledShifts);
        Assert.Equal(0, alexRow.CallOuts);
        Assert.Equal(2, alexRow.ShiftsCovered);
        Assert.Equal(
            [(Monday, "sam Tester", false), (Monday.AddDays(2), "jo Tester", true)],
            alexRow.CoverDetails.Select(d => (d.Date, d.CoveredForName, d.WasAlreadyWorking)).ToList());
        // Described by the shift that was covered, not the combined one.
        Assert.Equal("Evening", alexRow.CoverDetails[1].ShiftName);
        Assert.Equal(new TimeOnly(15, 0), alexRow.CoverDetails[1].ShiftStartTime);

        var joRow = rows["jo Tester"];
        Assert.Equal(2, joRow.ScheduledShifts);
        Assert.Equal(1, joRow.CallOuts);
        Assert.Equal(1, joRow.CallOutsCovered);
    }

    [Fact]
    public void Only_posted_shifts_inside_the_range_at_the_callers_location_count()
    {
        CallOut(evening, sam, Monday.AddDays(-1)); // before the range
        CallOut(evening, sam, Monday.AddDays(7)); // after it
        Assign(evening, sam, Monday, a => // a draft
        {
            a.IsPublished = false;
            a.IsAbsent = true;
        });
        var elsewhereShift = new Shift { Name = "Away", StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(17, 0), LocationId = otherLocation.Id };
        db.Shifts.Add(elsewhereShift);
        db.SaveChanges();
        CallOut(elsewhereShift, Seed("away", AccountRole.Employee, otherLocation), Monday);
        Assign(morning, alex, Monday);

        var rows = Run(Monday, Monday.AddDays(6));

        var row = Assert.Single(rows.Values);
        Assert.Equal("alex Tester", row.FullName);
        Assert.Equal(0, row.CallOuts);
    }

    [Fact]
    public void Someone_who_was_absent_from_the_shift_they_were_covering_gets_no_credit_for_it()
    {
        var absence = CallOut(evening, sam, Monday);
        Assign(evening, alex, Monday, a =>
        {
            a.CoversAssignmentId = absence.Id;
            a.IsAbsent = true;
            a.AbsenceNote = "No show";
        });

        var rows = Run(Monday, Monday);

        Assert.Equal(0, rows["alex Tester"].ShiftsCovered);
        Assert.Equal(1, rows["alex Tester"].CallOuts);
        // Sam's shift still shows who was meant to cover it.
        Assert.Equal(1, rows["sam Tester"].CallOutsCovered);
    }

    [Fact]
    public void It_is_admin_only_and_rejects_a_backwards_range()
    {
        var authorize = typeof(ReportsController).GetMethod(nameof(ReportsController.GetCallOutReport))!
            .GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), false)
            .Cast<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>()
            .Single();
        Assert.Equal("AdminOrAbove", authorize.Policy);

        Assert.IsType<BadRequestObjectResult>(As(admin).GetCallOutReport(null, Monday, Monday.AddDays(-1)).Result);
    }
}
