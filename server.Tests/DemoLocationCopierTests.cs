using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Server.Data;
using Server.Models;
using Server.Security;
using Server.Services;

namespace Server.Tests;

// The Super Admin "Create / Reset Demo" button: copies a real location into
// "demo" with everyone anonymized and email off, and a second run replaces
// the copy without touching the real location.
public sealed class DemoLocationCopierTests : IDisposable
{
    private static readonly DateOnly Monday = new(2026, 9, 14);

    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly DbContextOptions<AppDbContext> options;
    private readonly PinProtector pins = new(new EphemeralDataProtectionProvider());

    public DemoLocationCopierTests()
    {
        connection.Open();
        options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        using var db = new AppDbContext(options);
        db.Database.EnsureCreated();

        var real = new Location { Name = "Real Place", LocationCode = "real", Address = "1 Real St" };
        db.Locations.Add(real);
        db.SaveChanges();
        db.LocationSettings.Add(new LocationSettings
        {
            LocationId = real.Id,
            SmtpHost = "smtp.real.example",
            SmtpUsername = "office@real.example",
            ClockInAnywhere = false,
            OvertimePreset = OvertimePreset.California,
        });
        db.AllowedPunchDevices.Add(new AllowedPunchDevice { LocationId = real.Id, IpAddress = "203.0.113.5", Label = "Front desk" });

        var shift = new Shift { Name = "Morning", StartTime = new(7, 0), EndTime = new(15, 0), LocationId = real.Id };
        shift.ScheduledBreaks.Add(new ScheduledBreak { Kind = BreakKind.Lunch, StartTime = new(11, 0), EndTime = new(11, 30) });
        db.Shifts.Add(shift);

        var boss = Person(real, "Sonia", "Realname", AccountRole.Admin, "sonia");
        var sam = Person(real, "Samantha", "Truelast", AccountRole.Employee, "samantha.truelast");
        var alex = Person(real, "Alexander", "Actual", AccountRole.Employee, "alexander.actual");
        db.Accounts.AddRange(boss, sam, alex);
        db.SaveChanges();

        var absent = new ShiftAssignment
        {
            ShiftId = shift.Id, AccountId = sam.Id, Date = Monday, IsPublished = true,
            IsAbsent = true, AbsenceNote = "Samantha called Sonia at 6am", AbsentMarkedByAccountId = boss.Id,
        };
        db.ShiftAssignments.Add(absent);
        db.SaveChanges();
        var cover = new ShiftAssignment
        {
            ShiftId = shift.Id, AccountId = alex.Id, Date = Monday, IsPublished = true,
            CoversAssignmentId = absent.Id, CoverAssignedByAccountId = boss.Id,
        };
        db.ShiftAssignments.Add(cover);
        db.SaveChanges();

        var clockIn = new DateTime(2026, 9, 14, 14, 0, 0, DateTimeKind.Utc);
        db.TimeEntries.Add(new TimeEntry
        {
            AccountId = alex.Id, ShiftAssignmentId = cover.Id, ClockInAt = clockIn, ClockOutAt = clockIn.AddHours(8),
            Note = "Alexander stayed late for sonia", ClockedOutByAccountId = boss.Id,
            Segments = [new TimeEntrySegment { Kind = BreakKind.Lunch, StartAt = clockIn.AddHours(4), EndAt = clockIn.AddHours(4.5) }],
        });
        db.WriteUps.Add(new WriteUp { AccountId = sam.Id, CreatedByAccountId = boss.Id, Date = Monday, Description = "Late again" });
        db.SaveChanges();
    }

    public void Dispose() => connection.Dispose();

    private Account Person(Location at, string first, string last, AccountRole role, string username) => new()
    {
        Username = username,
        PasswordHash = PasswordHasher.Hash("real-password"),
        FirstName = first,
        LastName = last,
        Email = $"{first}@real.example",
        Phone = "408-555-9999",
        Role = role,
        LocationId = at.Id,
        UserCode = (100000 + first.Length).ToString(),
        DateOfBirth = new DateOnly(1990, 1, 2),
        BirthDate = "01/02",
        Address1 = "9 Home Rd",
        City = "San Jose",
        SsnLast4 = "1234",
        SsnEncrypted = "cipher",
        Supervisor = "Sonia Realname",
        PhotoFileName = "photo.jpg",
        HourlyRate = 20m,
        PinEncrypted = pins.Protect("4455"),
    };

    private void Copy()
    {
        using var db = new AppDbContext(options);
        new DemoLocationCopier(db, pins).CopyFrom("real", "DemoPass123", "1234");
    }

    [Fact]
    public void Copies_the_location_with_everyone_anonymized_and_email_off()
    {
        Copy();

        using var db = new AppDbContext(options);
        var demo = db.Locations.Single(l => l.LocationCode == "demo");
        var people = db.Accounts.Where(a => a.LocationId == demo.Id).ToList();
        Assert.Equal(3, people.Count);

        string[] realWords = ["Sonia", "Realname", "Samantha", "Truelast", "Alexander", "Actual", "real.example"];
        foreach (var p in people)
        {
            Assert.DoesNotContain(p.FirstName, realWords);
            Assert.DoesNotContain(p.LastName, realWords);
            Assert.EndsWith("@demo.example.com", p.Email);
            Assert.Empty(p.Phone);
            Assert.Null(p.DateOfBirth);
            Assert.Null(p.BirthDate);
            Assert.Null(p.Address1);
            Assert.Null(p.City);
            Assert.Null(p.SsnLast4);
            Assert.Null(p.SsnEncrypted);
            Assert.Null(p.Supervisor);
            Assert.Null(p.PhotoFileName);
            Assert.InRange(p.HourlyRate!.Value, 19m, 21m);
            Assert.Equal("4455", pins.Unprotect(p.PinEncrypted!));
        }

        var admin = people.Single(p => p.Role == AccountRole.Admin);
        Assert.StartsWith("demo.", admin.Username);
        Assert.True(PasswordHasher.Verify("DemoPass123", admin.PasswordHash));
        Assert.Equal(new[] { "100005", "100008", "100009" }, people.Select(p => p.UserCode!).Order());

        var settings = db.LocationSettings.Single(s => s.LocationId == demo.Id);
        Assert.Null(settings.SmtpHost);
        Assert.Null(settings.SmtpUsername);
        Assert.True(settings.ClockInAnywhere);
        Assert.Equal(OvertimePreset.California, settings.OvertimePreset);
        Assert.True(PasswordHasher.Verify("1234", settings.KioskPasscodeHash!));
        Assert.Empty(db.AllowedPunchDevices.Where(d => d.LocationId == demo.Id));

        var shift = db.Shifts.Include(s => s.ScheduledBreaks).Single(s => s.LocationId == demo.Id);
        Assert.Single(shift.ScheduledBreaks);

        var assignments = db.ShiftAssignments.Where(a => a.Shift!.LocationId == demo.Id).ToList();
        var absent = assignments.Single(a => a.IsAbsent);
        var cover = assignments.Single(a => a.CoversAssignmentId != null);
        Assert.Equal(absent.Id, cover.CoversAssignmentId);
        Assert.Equal(admin.Id, absent.AbsentMarkedByAccountId);
        Assert.DoesNotContain(realWords, w => absent.AbsenceNote!.Contains(w, StringComparison.OrdinalIgnoreCase));

        var entry = db.TimeEntries.Include(t => t.Segments).Single(t => t.ShiftAssignmentId == cover.Id);
        Assert.Single(entry.Segments);
        Assert.Equal(admin.Id, entry.ClockedOutByAccountId);
        Assert.DoesNotContain(realWords, w => entry.Note!.Contains(w, StringComparison.OrdinalIgnoreCase));

        Assert.Empty(db.WriteUps.Where(w => w.Account!.LocationId == demo.Id));
    }

    [Fact]
    public void Running_again_replaces_the_demo_and_leaves_the_real_location_alone()
    {
        Copy();
        string[] firstNames;
        using (var db = new AppDbContext(options))
        {
            firstNames = db.Accounts.Where(a => a.Location!.LocationCode == "demo").OrderBy(a => a.UserCode).Select(a => a.FirstName).ToArray();

            // Something added during a demo, which the next run must clear.
            var demoAdmin = db.Accounts.Single(a => a.Location!.LocationCode == "demo" && a.Role == AccountRole.Admin);
            var demoEmployee = db.Accounts.First(a => a.Location!.LocationCode == "demo" && a.Role == AccountRole.Employee);
            db.WriteUps.Add(new WriteUp { AccountId = demoEmployee.Id, CreatedByAccountId = demoAdmin.Id, Date = Monday, Description = "Demo" });
            db.SaveChanges();
        }

        Copy();

        using var check = new AppDbContext(options);
        Assert.Single(check.Locations.Where(l => l.LocationCode == "demo"));
        Assert.Equal(firstNames, check.Accounts.Where(a => a.Location!.LocationCode == "demo").OrderBy(a => a.UserCode).Select(a => a.FirstName).ToArray());
        Assert.Single(check.WriteUps);
        Assert.Equal(3, check.Accounts.Count(a => a.Location!.LocationCode == "real"));
        Assert.Equal(2, check.ShiftAssignments.Count(a => a.Shift!.Location!.LocationCode == "real"));
        Assert.Single(check.TimeEntries.Where(t => t.Account!.Location!.LocationCode == "real"));
        Assert.Equal("smtp.real.example", check.LocationSettings.Single(s => s.Location!.LocationCode == "real").SmtpHost);
        Assert.Equal("Samantha", check.Accounts.Single(a => a.Username == "samantha.truelast").FirstName);
    }
}
