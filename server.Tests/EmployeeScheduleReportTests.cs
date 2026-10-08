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

// ReportsController.GetEmployeeScheduleReport: one employee's posted shifts
// over a date range, absent or not.
public sealed class EmployeeScheduleReportTests : IDisposable
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

    public EmployeeScheduleReportTests()
    {
        connection.Open();
        db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        db.Database.EnsureCreated();

        location = new Location { Name = "Test", LocationCode = "t1" };
        otherLocation = new Location { Name = "Other", LocationCode = "t2" };
        db.Locations.AddRange(location, otherLocation);
        db.SaveChanges();

        morning = new Shift
        {
            Name = "Morning", StartTime = new TimeOnly(7, 0), EndTime = new TimeOnly(15, 0), LocationId = location.Id,
            ScheduledBreaks = [new ScheduledBreak { Kind = BreakKind.Lunch, StartTime = new TimeOnly(11, 0), EndTime = new TimeOnly(11, 30) }],
        };
        evening = new Shift { Name = "Evening", StartTime = new TimeOnly(15, 0), EndTime = new TimeOnly(23, 0), LocationId = location.Id };
        db.Shifts.AddRange(morning, evening);
        db.SaveChanges();

        admin = Seed("admin", AccountRole.Admin, location);
        sam = Seed("sam", AccountRole.Employee, location);
        alex = Seed("alex", AccountRole.Employee, location);
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

    private void Assign(Shift shift, Account account, DateOnly date, Action<ShiftAssignment>? configure = null)
    {
        var assignment = new ShiftAssignment { ShiftId = shift.Id, AccountId = account.Id, Date = date, IsPublished = true };
        configure?.Invoke(assignment);
        db.ShiftAssignments.Add(assignment);
        db.SaveChanges();
    }

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

    private List<ScheduledShiftDto> Run(Account employee, DateOnly start, DateOnly end) =>
        Assert.IsAssignableFrom<IEnumerable<ScheduledShiftDto>>(
            Assert.IsType<OkObjectResult>(As(admin).GetEmployeeScheduleReport(null, employee.Id, start, end).Result).Value)
            .ToList();

    [Fact]
    public void It_lists_the_employees_shifts_in_date_order_including_ones_they_were_absent_for()
    {
        Assign(evening, sam, Monday.AddDays(2));
        Assign(morning, sam, Monday);
        Assign(evening, sam, Monday.AddDays(1), a =>
        {
            a.IsAbsent = true;
            a.AbsenceNote = "Flu";
        });
        Assign(morning, alex, Monday); // someone else's

        var rows = Run(sam, Monday, Monday.AddDays(6));

        Assert.Equal(
            [(Monday, "Morning"), (Monday.AddDays(1), "Evening"), (Monday.AddDays(2), "Evening")],
            rows.Select(r => (r.Date, r.ShiftName)).ToList());
        Assert.Equal(new TimeOnly(7, 0), rows[0].ShiftStartTime);
        Assert.Equal(new TimeOnly(15, 0), rows[0].ShiftEndTime);
        // Shift span less the scheduled lunch.
        Assert.Equal(450, rows[0].ScheduledMinutes);
        Assert.Equal(480, rows[1].ScheduledMinutes);
    }

    [Fact]
    public void Only_posted_shifts_inside_the_range_are_listed()
    {
        Assign(morning, sam, Monday.AddDays(-1)); // before the range
        Assign(morning, sam, Monday.AddDays(7)); // after it
        Assign(morning, sam, Monday.AddDays(1), a => a.IsPublished = false); // a draft
        Assign(evening, sam, Monday);

        var row = Assert.Single(Run(sam, Monday, Monday.AddDays(6)));

        Assert.Equal(Monday, row.Date);
    }

    [Fact]
    public void It_is_admin_only_and_rejects_a_backwards_range_or_an_employee_from_another_location()
    {
        var authorize = typeof(ReportsController).GetMethod(nameof(ReportsController.GetEmployeeScheduleReport))!
            .GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), false)
            .Cast<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>()
            .Single();
        Assert.Equal("AdminOrAbove", authorize.Policy);

        Assert.IsType<BadRequestObjectResult>(
            As(admin).GetEmployeeScheduleReport(null, sam.Id, Monday, Monday.AddDays(-1)).Result);

        var away = Seed("away", AccountRole.Employee, otherLocation);
        Assert.IsType<BadRequestObjectResult>(
            As(admin).GetEmployeeScheduleReport(null, away.Id, Monday, Monday).Result);
    }
}
