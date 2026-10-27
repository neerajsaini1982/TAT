using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Server.Data;
using Server.Dtos;
using Server.Services;

namespace Server.Tests;

// AddTimeOffRequests adds three LocationSettings columns. A location that
// already had a settings row must come out with time off turned on and an
// 8-hour full day — not the zero/false a bare column default would give.
public sealed class TimeOffMigrationTests : IDisposable
{
    private const string BeforeTimeOff = "20261015160325_AddCanManageOthersSchedulesToAccount";

    private readonly SqliteConnection connection = new("Data Source=:memory:");

    public TimeOffMigrationTests() => connection.Open();

    public void Dispose() => connection.Dispose();

    [Fact]
    public void Existing_settings_get_time_off_on_with_an_eight_hour_day()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        using (var db = new AppDbContext(options))
        {
            db.GetService<IMigrator>().Migrate(BeforeTimeOff);
            db.Database.ExecuteSqlRaw(
                """
                INSERT INTO Locations (Name, Address, LocationCode, Phone, Email, IsActive, CreatedAt)
                VALUES ('L', 'a', 'm1', '', '', 1, '2026-09-01 00:00:00');
                INSERT INTO LocationSettings (LocationId, TimeFormat, DateFormat, TimeZone, AvailabilityDays, ClockInWindowMinutes,
                    LateClockInGraceMinutes, BreakLimitMinutes, LunchLimitMinutes, OvertimePreset, WorkweekStartDay, DevelopmentMode,
                    ClockInAnywhere, ScheduleVisibilityEnabled, AdminSeesAllSchedules, LeadSeesAllSchedules, EmployeeSeesAllSchedules, SmtpUseSsl)
                VALUES (1, 'TwelveHour', 'MmDdYyyy', 'America/Los_Angeles', 7, 15, 5, 15, 30, 'Custom', 'Monday', 0, 1, 1, 1, 0, 0, 1);
                """);
        }

        using (var db = new AppDbContext(options))
        {
            db.GetService<IMigrator>().Migrate();
        }

        using (var db = new AppDbContext(options))
        {
            var settings = db.LocationSettings.Single();
            Assert.True(settings.TimeOffEnabled);
            Assert.Equal(480, settings.TimeOffFullDayMinutes);
            Assert.Null(settings.TimeOffMinNoticeDays);
        }
    }
}

public sealed class TimeOffPayrollEmailTests
{
    [Fact]
    public void The_payroll_email_shows_paid_and_unpaid_time_off_and_the_breakdown()
    {
        var date = new DateOnly(2026, 11, 2);
        var day = new DailyHoursDto(
            date, null, 0, 0, null, null, 0, 0, 0, false, null, false, null, false, false, false, [], 0, null, [],
            PaidTimeOffMinutes: 480, UnpaidTimeOffMinutes: 0, TimeOffBreakdown: ["Vacation 8h"]);
        var row = new EmployeeHoursReportDto(
            1, "Sam T", false, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, [day], TotalPaidTimeOffMinutes: 480, TotalUnpaidTimeOffMinutes: 60);

        var html = PayrollHoursEmail.BuildHoursHtml(row, Server.Models.DateFormat.MmDdYyyy);

        Assert.Contains("Paid Time Off", html);
        Assert.Contains("Unpaid Time Off", html);
        Assert.Contains("Vacation 8h", html);
        Assert.Equal(480, PayrollHoursEmail.TotalPaidMinutes(row));
    }
}
