using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAbsenceAndAttendanceThresholds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ClockedOutByAccountId",
                table: "TimeEntries",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Note",
                table: "TimeEntries",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AbsenceNote",
                table: "ShiftAssignments",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsAbsent",
                table: "ShiftAssignments",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "BreakLimitMinutes",
                table: "LocationSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 15);

            migrationBuilder.AddColumn<int>(
                name: "LateClockInGraceMinutes",
                table: "LocationSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 5);

            migrationBuilder.AddColumn<int>(
                name: "LunchLimitMinutes",
                table: "LocationSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 30);

            migrationBuilder.CreateIndex(
                name: "IX_TimeEntries_ClockedOutByAccountId",
                table: "TimeEntries",
                column: "ClockedOutByAccountId");

            migrationBuilder.AddForeignKey(
                name: "FK_TimeEntries_Accounts_ClockedOutByAccountId",
                table: "TimeEntries",
                column: "ClockedOutByAccountId",
                principalTable: "Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TimeEntries_Accounts_ClockedOutByAccountId",
                table: "TimeEntries");

            migrationBuilder.DropIndex(
                name: "IX_TimeEntries_ClockedOutByAccountId",
                table: "TimeEntries");

            migrationBuilder.DropColumn(
                name: "ClockedOutByAccountId",
                table: "TimeEntries");

            migrationBuilder.DropColumn(
                name: "Note",
                table: "TimeEntries");

            migrationBuilder.DropColumn(
                name: "AbsenceNote",
                table: "ShiftAssignments");

            migrationBuilder.DropColumn(
                name: "IsAbsent",
                table: "ShiftAssignments");

            migrationBuilder.DropColumn(
                name: "BreakLimitMinutes",
                table: "LocationSettings");

            migrationBuilder.DropColumn(
                name: "LateClockInGraceMinutes",
                table: "LocationSettings");

            migrationBuilder.DropColumn(
                name: "LunchLimitMinutes",
                table: "LocationSettings");
        }
    }
}
