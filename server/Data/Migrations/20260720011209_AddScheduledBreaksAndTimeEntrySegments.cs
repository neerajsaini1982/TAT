using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddScheduledBreaksAndTimeEntrySegments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ScheduledBreaks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ShiftId = table.Column<int>(type: "INTEGER", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "TEXT", nullable: false),
                    EndTime = table.Column<TimeOnly>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduledBreaks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScheduledBreaks_Shifts_ShiftId",
                        column: x => x.ShiftId,
                        principalTable: "Shifts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TimeEntrySegments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TimeEntryId = table.Column<int>(type: "INTEGER", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    StartAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EndAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TimeEntrySegments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TimeEntrySegments_TimeEntries_TimeEntryId",
                        column: x => x.TimeEntryId,
                        principalTable: "TimeEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledBreaks_ShiftId",
                table: "ScheduledBreaks",
                column: "ShiftId");

            migrationBuilder.CreateIndex(
                name: "IX_TimeEntrySegments_TimeEntryId",
                table: "TimeEntrySegments",
                column: "TimeEntryId");

            // Data backfill, run while the old columns still exist below.
            //
            // Shift.IsBreakRequired/IsLunchRequired never had an attached
            // time before this migration (that's the whole point of this
            // feature) — so there's nothing precise to carry forward.
            // Insert a placeholder window for any shift that had the flag
            // on, so the requirement doesn't silently disappear; the admin
            // reviews/retimes it via the shift edit form afterward.
            migrationBuilder.Sql(
                "INSERT INTO ScheduledBreaks (ShiftId, Kind, StartTime, EndTime) " +
                "SELECT Id, 'Break', '10:00:00', '10:15:00' FROM Shifts WHERE IsBreakRequired = 1;");
            migrationBuilder.Sql(
                "INSERT INTO ScheduledBreaks (ShiftId, Kind, StartTime, EndTime) " +
                "SELECT Id, 'Lunch', '12:00:00', '12:30:00' FROM Shifts WHERE IsLunchRequired = 1;");

            // TimeEntry punches, unlike the above, are real recorded data —
            // copy every non-null pair over exactly as-is (both the old
            // Break and Break2 columns become BreakKind.Break segments).
            migrationBuilder.Sql(
                "INSERT INTO TimeEntrySegments (TimeEntryId, Kind, StartAt, EndAt) " +
                "SELECT Id, 'Break', BreakStartAt, BreakEndAt FROM TimeEntries WHERE BreakStartAt IS NOT NULL;");
            migrationBuilder.Sql(
                "INSERT INTO TimeEntrySegments (TimeEntryId, Kind, StartAt, EndAt) " +
                "SELECT Id, 'Lunch', LunchStartAt, LunchEndAt FROM TimeEntries WHERE LunchStartAt IS NOT NULL;");
            migrationBuilder.Sql(
                "INSERT INTO TimeEntrySegments (TimeEntryId, Kind, StartAt, EndAt) " +
                "SELECT Id, 'Break', Break2StartAt, Break2EndAt FROM TimeEntries WHERE Break2StartAt IS NOT NULL;");

            migrationBuilder.DropColumn(
                name: "Break2EndAt",
                table: "TimeEntries");

            migrationBuilder.DropColumn(
                name: "Break2StartAt",
                table: "TimeEntries");

            migrationBuilder.DropColumn(
                name: "BreakEndAt",
                table: "TimeEntries");

            migrationBuilder.DropColumn(
                name: "BreakStartAt",
                table: "TimeEntries");

            migrationBuilder.DropColumn(
                name: "LunchEndAt",
                table: "TimeEntries");

            migrationBuilder.DropColumn(
                name: "LunchStartAt",
                table: "TimeEntries");

            migrationBuilder.DropColumn(
                name: "IsBreakRequired",
                table: "Shifts");

            migrationBuilder.DropColumn(
                name: "IsLunchRequired",
                table: "Shifts");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ScheduledBreaks");

            migrationBuilder.DropTable(
                name: "TimeEntrySegments");

            migrationBuilder.AddColumn<DateTime>(
                name: "Break2EndAt",
                table: "TimeEntries",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "Break2StartAt",
                table: "TimeEntries",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "BreakEndAt",
                table: "TimeEntries",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "BreakStartAt",
                table: "TimeEntries",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LunchEndAt",
                table: "TimeEntries",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LunchStartAt",
                table: "TimeEntries",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsBreakRequired",
                table: "Shifts",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsLunchRequired",
                table: "Shifts",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }
    }
}
