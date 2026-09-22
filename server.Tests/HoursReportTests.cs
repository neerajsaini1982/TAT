using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Server.Controllers;
using Server.Data;
using Server.Dtos;
using Server.Models;

namespace Server.Tests;

// Runs ReportsController.GetHoursReport against an in-memory SQLite database
// so the workweek-spanning behavior is exercised end to end, not just in the
// calculator.
public sealed class HoursReportTests : IDisposable
{
    // 2026-09-14 is a Monday.
    private static readonly DateOnly Monday = new(2026, 9, 14);

    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly AppDbContext db;

    public HoursReportTests()
    {
        connection.Open();
        db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        db.Dispose();
        connection.Dispose();
    }

    private (Location Location, Shift Shift) SeedLocation(Action<LocationSettings>? configure = null)
    {
        var location = new Location { Name = "Test", LocationCode = "t1" };
        db.Locations.Add(location);
        db.SaveChanges();

        if (configure is not null)
        {
            var settings = new LocationSettings { LocationId = location.Id };
            configure(settings);
            db.LocationSettings.Add(settings);
        }

        var shift = new Shift { Name = "Day", StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(18, 0), LocationId = location.Id };
        db.Shifts.Add(shift);
        db.SaveChanges();
        return (location, shift);
    }

    private Account SeedEmployee(Location location, string username, bool exempt = false)
    {
        var account = new Account
        {
            Username = username,
            FirstName = username,
            LastName = "Tester",
            Role = AccountRole.Employee,
            LocationId = location.Id,
            IsOvertimeExempt = exempt,
        };
        db.Accounts.Add(account);
        db.SaveChanges();
        return account;
    }

    // A published, fully clocked-in-and-out shift of `hours` on `date`.
    private void SeedWorkedDay(Shift shift, Account account, DateOnly date, double hours)
    {
        var assignment = new ShiftAssignment { ShiftId = shift.Id, AccountId = account.Id, Date = date, IsPublished = true };
        db.ShiftAssignments.Add(assignment);
        db.SaveChanges();

        var clockIn = date.ToDateTime(new TimeOnly(8, 0), DateTimeKind.Utc);
        db.TimeEntries.Add(new TimeEntry
        {
            AccountId = account.Id,
            ShiftAssignmentId = assignment.Id,
            ClockInAt = clockIn,
            ClockOutAt = clockIn.AddMinutes(hours * 60),
        });
        db.SaveChanges();
    }

    private List<EmployeeHoursReportDto> RunReport(DateOnly start, DateOnly end)
    {
        var controller = new ReportsController(db)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim(ClaimTypes.NameIdentifier, "1"),
                        new Claim(ClaimTypes.Role, nameof(AccountRole.Sa)),
                    ], "test")),
                },
            },
        };

        var result = controller.GetHoursReport("t1", start, end);
        return ((OkObjectResult)result.Result!).Value is IEnumerable<EmployeeHoursReportDto> rows
            ? rows.ToList()
            : throw new InvalidOperationException("Report did not return rows.");
    }

    [Fact]
    public void A_location_with_no_settings_row_keeps_the_original_daily_rule()
    {
        var (location, shift) = SeedLocation();
        var employee = SeedEmployee(location, "a");
        SeedWorkedDay(shift, employee, Monday, 10);

        var row = Assert.Single(RunReport(Monday, Monday));

        Assert.Equal(8 * 60, row.TotalRegularMinutes);
        Assert.Equal(2 * 60, row.TotalOvertimeMinutes);
        Assert.Equal(0, row.TotalDoubleTimeMinutes);
    }

    [Fact]
    public void Weekly_overtime_counts_days_before_the_start_of_the_requested_range()
    {
        var (location, shift) = SeedLocation(s =>
        {
            s.OvertimePreset = OvertimePreset.Federal;
            s.OvertimeDailyThresholdMinutes = null;
            s.WeeklyOvertimeAfterMinutes = 40 * 60;
        });
        var employee = SeedEmployee(location, "a");
        // Mon..Fri, 9h each = 45h. The report only asks for Friday.
        for (var offset = 0; offset < 5; offset++)
        {
            SeedWorkedDay(shift, employee, Monday.AddDays(offset), 9);
        }

        var row = Assert.Single(RunReport(Monday.AddDays(4), Monday.AddDays(4)));

        // Monday-Thursday already used 36 regular hours, so only 4h of Friday's 9h is regular.
        var friday = Assert.Single(row.Days);
        Assert.Equal(4 * 60, friday.RegularMinutes);
        Assert.Equal(5 * 60, friday.OvertimeMinutes);
        // The report totals cover just the requested range, not the padded week.
        Assert.Equal(9 * 60, row.TotalNetWorkedMinutes);
        Assert.Equal(4 * 60, row.TotalRegularMinutes);
        Assert.Equal(5 * 60, row.TotalOvertimeMinutes);
    }

    [Fact]
    public void Days_outside_the_range_do_not_appear_and_do_not_add_employees()
    {
        var (location, shift) = SeedLocation(s => s.WeeklyOvertimeAfterMinutes = 40 * 60);
        var worksInRange = SeedEmployee(location, "in");
        var worksOnlyEarlierInWeek = SeedEmployee(location, "out");
        SeedWorkedDay(shift, worksInRange, Monday.AddDays(2), 6);
        SeedWorkedDay(shift, worksOnlyEarlierInWeek, Monday, 6);

        var rows = RunReport(Monday.AddDays(2), Monday.AddDays(3));

        var row = Assert.Single(rows);
        Assert.Equal("in Tester", row.FullName);
        Assert.Equal([Monday.AddDays(2)], row.Days.Select(d => d.Date));
    }

    [Fact]
    public void California_rules_produce_double_time_on_the_report()
    {
        var (location, shift) = SeedLocation(s =>
        {
            var policy = OvertimePolicy.ForPreset(OvertimePreset.California);
            s.OvertimePreset = OvertimePreset.California;
            s.OvertimeDailyThresholdMinutes = policy.DailyOvertimeAfterMinutes;
            s.DailyDoubleTimeAfterMinutes = policy.DailyDoubleTimeAfterMinutes;
            s.WeeklyOvertimeAfterMinutes = policy.WeeklyOvertimeAfterMinutes;
            s.SeventhDayDoubleTimeAfterMinutes = policy.SeventhDayDoubleTimeAfterMinutes;
        });
        var employee = SeedEmployee(location, "a");
        SeedWorkedDay(shift, employee, Monday, 13);

        var row = Assert.Single(RunReport(Monday, Monday));

        Assert.Equal(8 * 60, row.TotalRegularMinutes);
        Assert.Equal(4 * 60, row.TotalOvertimeMinutes);
        Assert.Equal(1 * 60, row.TotalDoubleTimeMinutes);
    }

    [Fact]
    public void A_sunday_start_workweek_changes_which_days_make_up_the_week()
    {
        // Sun 9/13 + Mon-Thu = 5 days of 9h. With a Sunday-start week that's
        // one 45h week; with the default Monday start Sunday is the previous
        // week's last day, and the Mon-Thu week is only 36h.
        var (location, shift) = SeedLocation(s =>
        {
            s.OvertimeDailyThresholdMinutes = null;
            s.WeeklyOvertimeAfterMinutes = 40 * 60;
            s.WorkweekStartDay = DayOfWeek.Sunday;
        });
        var employee = SeedEmployee(location, "a");
        var sunday = Monday.AddDays(-1);
        for (var offset = 0; offset < 5; offset++)
        {
            SeedWorkedDay(shift, employee, sunday.AddDays(offset), 9);
        }

        var row = Assert.Single(RunReport(sunday, Monday.AddDays(3)));

        Assert.Equal(5 * 60, row.TotalOvertimeMinutes);
    }

    [Fact]
    public void An_exempt_employee_gets_all_regular_time_while_others_at_the_location_still_get_overtime()
    {
        var (location, shift) = SeedLocation(s =>
        {
            var policy = OvertimePolicy.ForPreset(OvertimePreset.California);
            s.OvertimePreset = OvertimePreset.California;
            s.OvertimeDailyThresholdMinutes = policy.DailyOvertimeAfterMinutes;
            s.DailyDoubleTimeAfterMinutes = policy.DailyDoubleTimeAfterMinutes;
            s.WeeklyOvertimeAfterMinutes = policy.WeeklyOvertimeAfterMinutes;
            s.SeventhDayDoubleTimeAfterMinutes = policy.SeventhDayDoubleTimeAfterMinutes;
        });
        var salaried = SeedEmployee(location, "salaried", exempt: true);
        var hourly = SeedEmployee(location, "hourly");
        SeedWorkedDay(shift, salaried, Monday, 13);
        SeedWorkedDay(shift, hourly, Monday, 13);

        var rows = RunReport(Monday, Monday).ToDictionary(r => r.FullName);

        var exempt = rows["salaried Tester"];
        Assert.True(exempt.IsOvertimeExempt);
        Assert.Equal(13 * 60, exempt.TotalRegularMinutes);
        Assert.Equal(0, exempt.TotalOvertimeMinutes);
        Assert.Equal(0, exempt.TotalDoubleTimeMinutes);
        Assert.Equal(13 * 60, exempt.TotalNetWorkedMinutes);

        var nonExempt = rows["hourly Tester"];
        Assert.False(nonExempt.IsOvertimeExempt);
        Assert.Equal(8 * 60, nonExempt.TotalRegularMinutes);
        Assert.Equal(4 * 60, nonExempt.TotalOvertimeMinutes);
        Assert.Equal(1 * 60, nonExempt.TotalDoubleTimeMinutes);
    }

    [Fact]
    public void An_exempt_employee_is_exempt_from_the_weekly_rule_too()
    {
        var (location, shift) = SeedLocation(s =>
        {
            s.OvertimePreset = OvertimePreset.Federal;
            s.OvertimeDailyThresholdMinutes = null;
            s.WeeklyOvertimeAfterMinutes = 40 * 60;
        });
        var salaried = SeedEmployee(location, "salaried", exempt: true);
        for (var offset = 0; offset < 5; offset++)
        {
            SeedWorkedDay(shift, salaried, Monday.AddDays(offset), 10);
        }

        var row = Assert.Single(RunReport(Monday, Monday.AddDays(4)));

        Assert.Equal(50 * 60, row.TotalRegularMinutes);
        Assert.Equal(0, row.TotalOvertimeMinutes);
    }
}
