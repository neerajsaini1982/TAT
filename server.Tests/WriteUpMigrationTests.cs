using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Server.Data;
using Server.Models;

namespace Server.Tests;

// The follow-up migration adds NOT NULL enum-as-string columns to a table
// that may already have rows. EF's generated default for those is "", which
// can't be read back as an enum — so this runs the real migrations over a
// database that already holds a write-up, which EnsureCreated (used by the
// other tests) never exercises.
public sealed class WriteUpMigrationTests : IDisposable
{
    private const string BeforeFollowUp = "20260920220147_AddWriteUps";

    private readonly SqliteConnection connection = new("Data Source=:memory:");

    public WriteUpMigrationTests() => connection.Open();

    public void Dispose() => connection.Dispose();

    [Fact]
    public void A_write_up_that_existed_before_the_follow_up_migration_is_still_readable_and_has_history()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        using (var db = new AppDbContext(options))
        {
            db.GetService<IMigrator>().Migrate(BeforeFollowUp);

            // Raw SQL: the entity no longer matches the schema at this point.
            db.Database.ExecuteSqlRaw(
                """
                INSERT INTO Locations (Name, Address, LocationCode, Phone, Email, IsActive, CreatedAt)
                VALUES ('L', 'a', 'm1', '', '', 1, '2026-09-01 00:00:00');
                INSERT INTO Accounts (Username, PasswordHash, FirstName, LastName, Email, Phone, Role, IsActive,
                                      IsOnShiftSchedule, CanSeeAllSchedules, CreatedAt, LocationId, IsOvertimeExempt, PinFailedAttempts)
                VALUES ('adm', 'x', 'A', 'Dmin', '', '', 'Admin', 1, 1, 0, '2026-09-01 00:00:00', 1, 0, 0),
                       ('emp', 'x', 'E', 'Mp', '', '', 'Employee', 1, 1, 0, '2026-09-01 00:00:00', 1, 0, 0);
                INSERT INTO WriteUps (AccountId, Date, Description, Severity, CreatedByAccountId, CreatedAt)
                VALUES (2, '2026-09-10', 'Pre-existing write-up', 'High', 1, '2026-09-11 08:00:00');
                """);
        }

        using (var db = new AppDbContext(options))
        {
            db.GetService<IMigrator>().Migrate();
        }

        using (var db = new AppDbContext(options))
        {
            var writeUp = db.WriteUps.Include(w => w.Events).Single();

            Assert.Equal("Pre-existing write-up", writeUp.Description);
            Assert.Equal(WriteUpSeverity.High, writeUp.Severity);
            Assert.Equal(WriteUpType.Written, writeUp.Type);
            Assert.Equal(WriteUpAcknowledgment.Pending, writeUp.AcknowledgmentStatus);
            Assert.False(writeUp.IsVoided);

            var created = Assert.Single(writeUp.Events);
            Assert.Equal(WriteUpEventAction.Created, created.Action);
            Assert.Equal(1, created.ByAccountId);
            Assert.Equal(writeUp.CreatedAt, created.At);
        }
    }
}
