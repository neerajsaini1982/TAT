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

// ReportsController.EmailHoursReport: one PayrollHours email per employee
// with an address, carrying the same numbers GetHoursReport shows.
public sealed class EmailHoursReportTests : IDisposable
{
    // 2026-09-21 is a Monday.
    private static readonly DateOnly Monday = new(2026, 9, 21);

    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly AppDbContext db;
    private readonly RecordingEmailSender sender = new();

    public EmailHoursReportTests()
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

    private Shift SeedLocation(string? smtpHost = "smtp.test")
    {
        var location = new Location { Name = "Test Store", LocationCode = "t1" };
        db.Locations.Add(location);
        db.SaveChanges();

        db.LocationSettings.Add(new LocationSettings { LocationId = location.Id, SmtpHost = smtpHost });
        var shift = new Shift { Name = "Day", StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(12, 0), LocationId = location.Id };
        db.Shifts.Add(shift);
        db.SaveChanges();
        return shift;
    }

    private Account SeedEmployee(Shift shift, string firstName, string email, params DateOnly[] workedDays)
    {
        var account = new Account
        {
            Username = firstName,
            FirstName = firstName,
            LastName = "Tester",
            Email = email,
            Role = AccountRole.Employee,
            LocationId = shift.LocationId,
        };
        db.Accounts.Add(account);
        db.SaveChanges();

        foreach (var date in workedDays)
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
                ClockOutAt = clockIn.AddHours(4),
            });
            db.SaveChanges();
        }

        return account;
    }

    private ActionResult<EmailHoursReportResultDto> Send(EmailHoursReportRequest request)
    {
        var controller = new ReportsController(db, sender)
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

        return controller.EmailHoursReport(request).GetAwaiter().GetResult();
    }

    private static EmailHoursReportResultDto Ok(ActionResult<EmailHoursReportResultDto> result) =>
        (EmailHoursReportResultDto)((OkObjectResult)result.Result!).Value!;

    [Fact]
    public void Each_employee_with_an_email_gets_their_own_hours_and_the_rest_are_reported_as_skipped()
    {
        var shift = SeedLocation();
        SeedEmployee(shift, "Ann", "ann@test.com", Monday, Monday.AddDays(1));
        SeedEmployee(shift, "Bob", "", Monday);

        var result = Ok(Send(new EmailHoursReportRequest("t1", Monday, Monday.AddDays(6), null, null)));

        Assert.Equal(["Ann Tester"], result.Sent);
        Assert.Equal(["Bob Tester"], result.SkippedNoEmail);
        Assert.Empty(result.Failed);

        var email = Assert.Single(sender.Sent);
        Assert.Equal("ann@test.com", email.To);
        Assert.Equal("Your hours for 09/21/2026 – 09/27/2026", email.Subject);
        Assert.Contains("Hi Ann Tester", email.BodyHtml);
        Assert.Contains("Monday 09/21/2026", email.BodyHtml);
        Assert.Contains("Tuesday 09/22/2026", email.BodyHtml);
        Assert.Contains("8.00", email.BodyHtml);
        Assert.Contains("8h 0m", email.BodyHtml);
        Assert.DoesNotContain("{{", email.BodyHtml);
    }

    [Fact]
    public void EmployeeIds_limits_who_is_emailed()
    {
        var shift = SeedLocation();
        SeedEmployee(shift, "Ann", "ann@test.com", Monday);
        var bob = SeedEmployee(shift, "Bob", "bob@test.com", Monday);

        var result = Ok(Send(new EmailHoursReportRequest("t1", Monday, Monday, [bob.Id], null)));

        Assert.Equal(["Bob Tester"], result.Sent);
        Assert.Equal("bob@test.com", Assert.Single(sender.Sent).To);
    }

    [Fact]
    public void A_test_send_goes_only_to_the_test_address_with_real_data()
    {
        var shift = SeedLocation();
        SeedEmployee(shift, "Ann", "ann@test.com", Monday);
        SeedEmployee(shift, "Bob", "bob@test.com", Monday);

        var result = Ok(Send(new EmailHoursReportRequest("t1", Monday, Monday, null, "admin@test.com")));

        Assert.Equal(["Ann Tester"], result.Sent);
        var email = Assert.Single(sender.Sent);
        Assert.Equal("admin@test.com", email.To);
        Assert.StartsWith("[TEST] ", email.Subject);
        Assert.Contains("Hi Ann Tester", email.BodyHtml);
    }

    [Fact]
    public void Nothing_is_sent_without_smtp()
    {
        var shift = SeedLocation(smtpHost: null);
        SeedEmployee(shift, "Ann", "ann@test.com", Monday);

        Assert.IsType<BadRequestObjectResult>(Send(new EmailHoursReportRequest("t1", Monday, Monday, null, null)).Result);
        Assert.Empty(sender.Sent);
    }
}
