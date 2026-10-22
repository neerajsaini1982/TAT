using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Server.Data;
using Server.Dtos;
using Server.Models;
using Server.Security;
using Server.Services;

namespace Server.Tests;

// The Super Admin "Create / Reset Demo" button: builds a clinic with staff,
// a published schedule and punches, and a second run wipes and rebuilds it
// without touching any other location.
public sealed class DemoLocationSeederTests : IDisposable
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly DbContextOptions<AppDbContext> options;
    private readonly PinProtector pins = new(new EphemeralDataProtectionProvider());

    public DemoLocationSeederTests()
    {
        connection.Open();
        options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        using var db = new AppDbContext(options);
        db.Database.EnsureCreated();
    }

    public void Dispose() => connection.Dispose();

    private DemoLocationResult Reset()
    {
        using var db = new AppDbContext(options);
        return new DemoLocationSeeder(db, pins).Reset("DemoPass123", "1234");
    }

    [Fact]
    public void Creates_a_clinic_with_staff_schedule_and_punches()
    {
        var result = Reset();

        using var db = new AppDbContext(options);
        var location = db.Locations.Single(l => l.LocationCode == "demo");
        Assert.Equal("Demo", location.Name);
        Assert.Equal(13, db.Accounts.Count(a => a.LocationId == location.Id));
        Assert.Equal(13, result.Logins.Count);
        Assert.Contains(result.Logins, l => l.Role == "Admin" && l.Username == "demo.admin");
        Assert.Contains(result.Logins, l => l.Role == "Lead" && l.Username == "demo.lead");

        var admin = db.Accounts.Single(a => a.Username == "demo.admin");
        Assert.True(PasswordHasher.Verify("DemoPass123", admin.PasswordHash));

        var settings = db.LocationSettings.Single(s => s.LocationId == location.Id);
        Assert.Equal(OvertimePreset.California, settings.OvertimePreset);
        Assert.True(PasswordHasher.Verify("1234", settings.KioskPasscodeHash!));

        Assert.True(db.ShiftAssignments.All(a => a.IsPublished));
        Assert.True(db.TimeEntries.Count() > 100);
        Assert.Equal(2, db.ShiftAssignments.Count(a => a.IsAbsent));
        Assert.Single(db.ShiftAssignments.Where(a => a.CoversAssignmentId != null));
        Assert.Single(db.TimeEntries.Where(t => t.LeftEarly));
    }

    [Fact]
    public void Reset_rebuilds_the_demo_and_leaves_other_locations_alone()
    {
        int otherAccountId;
        using (var db = new AppDbContext(options))
        {
            var other = new Location { Name = "Real Customer", LocationCode = "real" };
            db.Locations.Add(other);
            db.SaveChanges();
            var account = new Account { Username = "real.person", FirstName = "Real", LastName = "Person", Role = AccountRole.Employee, LocationId = other.Id };
            db.Accounts.Add(account);
            db.SaveChanges();
            otherAccountId = account.Id;
        }

        Reset();

        // Something a prospect might add during a demo, which the next reset
        // must clear even though write-ups are otherwise never deleted.
        using (var db = new AppDbContext(options))
        {
            var admin = db.Accounts.Single(a => a.Username == "demo.admin");
            var employee = db.Accounts.First(a => a.LocationId == admin.LocationId && a.Role == AccountRole.Employee);
            db.WriteUps.Add(new WriteUp { AccountId = employee.Id, CreatedByAccountId = admin.Id, Description = "Late", Date = DateOnly.FromDateTime(DateTime.UtcNow) });
            db.SaveChanges();
        }

        var counts = Counts();
        Reset();

        Assert.Equal(counts, Counts());
        using var check = new AppDbContext(options);
        Assert.Single(check.Locations.Where(l => l.LocationCode == "demo"));
        Assert.Empty(check.WriteUps);
        Assert.NotNull(check.Accounts.Find(otherAccountId));
        Assert.Single(check.Locations.Where(l => l.LocationCode == "real"));
    }

    private (int Accounts, int Shifts, int Assignments) Counts()
    {
        using var db = new AppDbContext(options);
        return (db.Accounts.Count(), db.Shifts.Count(), db.ShiftAssignments.Count());
    }
}
