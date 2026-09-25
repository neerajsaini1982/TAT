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

// AvailabilityController.SendReminder: only people who haven't submitted
// for the week are emailed, and anyone without an address is reported back.
public sealed class AvailabilityReminderTests : IDisposable
{
    private static readonly DateOnly Week = new(2026, 9, 28);

    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly AppDbContext db;
    private readonly RecordingEmailSender sender = new();

    public AvailabilityReminderTests()
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

    private Location SeedLocation()
    {
        var location = new Location { Name = "Test Store", LocationCode = "t1" };
        db.Locations.Add(location);
        db.SaveChanges();
        db.LocationSettings.Add(new LocationSettings { LocationId = location.Id, SmtpHost = "smtp.test" });
        db.SaveChanges();
        return location;
    }

    private Account SeedEmployee(Location location, string firstName, string email, bool submitted = false, bool active = true)
    {
        var account = new Account
        {
            Username = firstName,
            FirstName = firstName,
            LastName = "Tester",
            Email = email,
            Role = AccountRole.Employee,
            LocationId = location.Id,
            IsActive = active,
        };
        db.Accounts.Add(account);
        db.SaveChanges();

        if (submitted)
        {
            db.Availabilities.Add(new Availability { AccountId = account.Id, WeekStartDate = Week, IsSubmitted = true });
            db.SaveChanges();
        }

        return account;
    }

    private AvailabilityReminderResult Send()
    {
        var controller = new AvailabilityController(db, sender)
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

        var result = controller.SendReminder(new SendAvailabilityReminderRequest("t1", Week, "https://app.test/t1/employee/availability"))
            .GetAwaiter().GetResult();
        return (AvailabilityReminderResult)((OkObjectResult)result.Result!).Value!;
    }

    [Fact]
    public void Only_unsubmitted_active_employees_with_an_email_are_reminded()
    {
        var location = SeedLocation();
        SeedEmployee(location, "Ann", "ann@test.com");
        SeedEmployee(location, "Bob", "bob@test.com", submitted: true);
        SeedEmployee(location, "Cat", "");
        SeedEmployee(location, "Dan", "dan@test.com", active: false);

        var result = Send();

        Assert.Equal(["Ann Tester"], result.Sent);
        Assert.Equal(["Cat Tester"], result.SkippedNoEmail);
        Assert.Empty(result.Failed);
        Assert.Equal(1, result.AlreadySubmitted);

        var email = Assert.Single(sender.Sent);
        Assert.Equal("ann@test.com", email.To);
        Assert.Equal("Submit your availability for Sep 28 – Oct 4", email.Subject);
        Assert.Contains("href=\"https://app.test/t1/employee/availability\"", email.BodyHtml);
        Assert.DoesNotContain("{{", email.BodyHtml);
    }
}
