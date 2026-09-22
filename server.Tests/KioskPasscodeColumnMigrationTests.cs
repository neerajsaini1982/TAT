using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Server.Data;
using Server.Models;

namespace Server.Tests;

// AddKioskSupport added LocationSettings.KioskPasscodeHash, but AddOvertimeRules
// later rebuilt that table (SQLite's only way to drop several columns at once)
// using a target schema that didn't know about it — that migration was
// scaffolded on a branch that hadn't yet merged kiosk support. No later
// migration re-added it, so any database that ran the full chain (including a
// fresh production database on first deploy) ended up missing a column the
// C# model still declares. RestoreKioskPasscodeHashColumn is the fix; this
// runs the real migrations end to end, the way Program.cs does, to catch a
// repeat of this — EnsureCreated (used by most other tests) builds straight
// from the current model and would never notice a gap like this in between.
public sealed class KioskPasscodeColumnMigrationTests : IDisposable
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");

    public KioskPasscodeColumnMigrationTests() => connection.Open();

    public void Dispose() => connection.Dispose();

    [Fact]
    public void AddOvertimeRules_drops_the_column_the_fix_migration_restores()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        using var db = new AppDbContext(options);
        var migrator = db.GetService<IMigrator>();

        migrator.Migrate("20260823205118_AddKioskSupport");
        Assert.True(HasKioskPasscodeHashColumn(db));

        migrator.Migrate("20260918212817_AddOvertimeRules");
        Assert.False(HasKioskPasscodeHashColumn(db), "AddOvertimeRules was expected to drop the column — if this now fails, some other migration already fixed it upstream and RestoreKioskPasscodeHashColumn's comment explaining why it exists needs updating.");

        migrator.Migrate();
        Assert.True(HasKioskPasscodeHashColumn(db));
    }

    // The regression that actually matters: a database built from nothing,
    // through the whole chain in one go — exactly what happens the first
    // time this code deploys anywhere new — ends up with a LocationSettings
    // the EF model can fully round-trip, kiosk passcode included.
    [Fact]
    public void A_fresh_database_built_through_the_full_chain_can_store_and_read_a_kiosk_passcode()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        using (var db = new AppDbContext(options))
        {
            db.GetService<IMigrator>().Migrate();
        }

        using (var db = new AppDbContext(options))
        {
            var location = new Location { Name = "L", LocationCode = "m1" };
            db.Locations.Add(location);
            db.SaveChanges();

            db.LocationSettings.Add(new LocationSettings { LocationId = location.Id, KioskPasscodeHash = "hashed-passcode" });
            db.SaveChanges();
        }

        using (var db = new AppDbContext(options))
        {
            Assert.Equal("hashed-passcode", db.LocationSettings.Single().KioskPasscodeHash);
        }
    }

    private static bool HasKioskPasscodeHashColumn(AppDbContext db)
    {
        using var command = ((SqliteConnection)db.Database.GetDbConnection()).CreateCommand();
        command.CommandText = "SELECT count(*) FROM pragma_table_info('LocationSettings') WHERE name = 'KioskPasscodeHash'";
        return Convert.ToInt32(command.ExecuteScalar()) > 0;
    }
}
