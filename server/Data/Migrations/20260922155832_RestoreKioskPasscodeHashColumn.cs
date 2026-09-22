using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Data.Migrations
{
    // Hand-written, not scaffolded: `dotnet ef migrations add` sees no
    // change here, because the model and AppDbContextModelSnapshot.cs
    // already agree that LocationSettings.KioskPasscodeHash exists — that
    // snapshot is a full reflection of the current C# model taken whenever
    // any migration is scaffolded, not a replay of what each migration's
    // Up() actually did. It genuinely diverged from reality: AddKioskSupport
    // (20260823) added the column, but AddOvertimeRules (20260918) later
    // rebuilt this table (SQLite's only way to drop several columns at
    // once) using a target schema that didn't include it — that migration
    // was scaffolded on a branch that hadn't yet merged kiosk support, so
    // its own Designer.cs snapshot never knew the column existed. Verified
    // by generating that migration's actual SQL (`dotnet ef migrations
    // script`): the rebuilt table's CREATE TABLE and the copying INSERT
    // both omit KioskPasscodeHash, and no later migration re-adds it, so
    // any database that runs the full chain ends up missing it — including
    // a fresh production database on first deploy, since `main` has never
    // run AddKioskSupport or AddOvertimeRules. Confirmed by building a
    // fresh database through every migration on `development` (see
    // KioskPasscodeColumnMigrationTests) and separately by generating
    // AddOvertimeRules' own SQL script and reading it directly.
    //
    // This only adds the column back; it can't recover a passcode a
    // database lost when AddOvertimeRules ran (the copying INSERT never
    // carried the value forward), so any location that already had a kiosk
    // passcode set needs it set again after this runs.
    //
    // Deliberately a plain AddColumn, not a guarded/conditional one: EF's
    // runtime migrator requires every Migration subclass to have a
    // parameterless constructor (it builds the full pending list up front,
    // via reflection, before running any of them), so there's no supported
    // way for a migration to check the database first — I tried
    // constructor-injecting AppDbContext to do exactly that, and it crashed
    // the app on startup outright, for every database, migration or not.
    // If some other already-migrated database also has this column already
    // (from, e.g., a stale local file predating this fix, the way mine
    // did), reconcile it by hand: insert this migration's id into
    // __EFMigrationsHistory instead of letting Migrate() run its DDL.
    public partial class RestoreKioskPasscodeHashColumn : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "KioskPasscodeHash",
                table: "LocationSettings",
                type: "TEXT",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "KioskPasscodeHash",
                table: "LocationSettings");
        }
    }
}
