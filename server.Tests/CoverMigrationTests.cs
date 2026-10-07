using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Server.Data;
using Server.Models;

namespace Server.Tests;

// AddCoverToShiftAssignments adds foreign keys to ShiftAssignments, which on
// SQLite means rebuilding the table. This runs the real migrations over a
// database that already holds an assignment — EnsureCreated (used by the
// other tests) never exercises that — and checks the rebuilt table still
// has its rows and enforces the new cover link.
public sealed class CoverMigrationTests : IDisposable
{
    private const string BeforeCover = "20261005000935_AddPayPeriodStartDateToLocationSettings";

    private readonly SqliteConnection connection = new("Data Source=:memory:");

    public CoverMigrationTests() => connection.Open();

    public void Dispose() => connection.Dispose();

    [Fact]
    public void An_assignment_that_existed_before_the_migration_survives_it_and_can_be_covered()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        using (var db = new AppDbContext(options))
        {
            db.GetService<IMigrator>().Migrate(BeforeCover);

            // Raw SQL: the entity no longer matches the schema at this point.
            db.Database.ExecuteSqlRaw(
                """
                INSERT INTO Locations (Name, Address, LocationCode, Phone, Email, IsActive, CreatedAt)
                VALUES ('L', 'a', 'm1', '', '', 1, '2026-09-01 00:00:00');
                INSERT INTO Accounts (Username, PasswordHash, FirstName, LastName, Email, Phone, Role, IsActive,
                                      IsOnShiftSchedule, CanSeeAllSchedules, CanWriteUpOthers, CreatedAt, LocationId,
                                      IsOvertimeExempt, PinFailedAttempts)
                VALUES ('sam', 'x', 'Sam', 'R', '', '', 'Employee', 1, 1, 0, 0, '2026-09-01 00:00:00', 1, 0, 0),
                       ('alex', 'x', 'Alex', 'M', '', '', 'Employee', 1, 1, 0, 0, '2026-09-01 00:00:00', 1, 0, 0);
                INSERT INTO Shifts (Name, StartTime, EndTime, IsActive, CreatedAt, LocationId)
                VALUES ('Day', '08:00:00', '16:00:00', 1, '2026-09-01 00:00:00', 1);
                INSERT INTO ShiftAssignments (ShiftId, AccountId, Date, CreatedAt, IsPublished, IsAbsent, AbsenceNote, SickMinutes)
                VALUES (1, 1, '2026-09-14', '2026-09-10 00:00:00', 1, 1, 'Called in sick', 120);
                """);
        }

        using (var db = new AppDbContext(options))
        {
            db.GetService<IMigrator>().Migrate();
        }

        int absentId;
        using (var db = new AppDbContext(options))
        {
            var absent = db.ShiftAssignments.Single();
            absentId = absent.Id;

            Assert.True(absent.IsAbsent);
            Assert.Equal("Called in sick", absent.AbsenceNote);
            Assert.Equal(120, absent.SickMinutes);
            Assert.True(absent.IsPublished);
            Assert.Null(absent.CoversAssignmentId);
            Assert.Null(absent.OriginalShiftId);

            db.ShiftAssignments.Add(new ShiftAssignment
            {
                ShiftId = absent.ShiftId, AccountId = 2, Date = absent.Date, IsPublished = true, CoversAssignmentId = absent.Id,
            });
            db.SaveChanges();
        }

        // Deleting the absent assignment leaves the cover shift, unlinked.
        using (var db = new AppDbContext(options))
        {
            db.ShiftAssignments.Remove(db.ShiftAssignments.Single(a => a.Id == absentId));
            db.SaveChanges();
        }

        using (var db = new AppDbContext(options))
        {
            var cover = db.ShiftAssignments.Single();
            Assert.Equal(2, cover.AccountId);
            Assert.Null(cover.CoversAssignmentId);
        }
    }
}
