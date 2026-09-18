using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOvertimeRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "OvertimeDailyThresholdMinutes",
                table: "LocationSettings",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.AddColumn<int>(
                name: "DailyDoubleTimeAfterMinutes",
                table: "LocationSettings",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OvertimePreset",
                table: "LocationSettings",
                type: "TEXT",
                nullable: false,
                defaultValue: "Custom");

            migrationBuilder.AddColumn<int>(
                name: "SeventhDayDoubleTimeAfterMinutes",
                table: "LocationSettings",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WeeklyOvertimeAfterMinutes",
                table: "LocationSettings",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WorkweekStartDay",
                table: "LocationSettings",
                type: "TEXT",
                nullable: false,
                defaultValue: "Monday");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DailyDoubleTimeAfterMinutes",
                table: "LocationSettings");

            migrationBuilder.DropColumn(
                name: "OvertimePreset",
                table: "LocationSettings");

            migrationBuilder.DropColumn(
                name: "SeventhDayDoubleTimeAfterMinutes",
                table: "LocationSettings");

            migrationBuilder.DropColumn(
                name: "WeeklyOvertimeAfterMinutes",
                table: "LocationSettings");

            migrationBuilder.DropColumn(
                name: "WorkweekStartDay",
                table: "LocationSettings");

            migrationBuilder.AlterColumn<int>(
                name: "OvertimeDailyThresholdMinutes",
                table: "LocationSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 480,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);
        }
    }
}
