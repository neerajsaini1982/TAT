using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddScheduleVisibilityToLocationSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Admin keeps seeing everyone by default (matching current
            // behavior via the admin roster page), and the master switch
            // defaults on — both per LocationSettings' C# defaults, so
            // existing rows come out of this migration behaving exactly
            // like new ones do.
            migrationBuilder.AddColumn<bool>(
                name: "AdminSeesAllSchedules",
                table: "LocationSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "EmployeeSeesAllSchedules",
                table: "LocationSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "LeadSeesAllSchedules",
                table: "LocationSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ScheduleVisibilityEnabled",
                table: "LocationSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AdminSeesAllSchedules",
                table: "LocationSettings");

            migrationBuilder.DropColumn(
                name: "EmployeeSeesAllSchedules",
                table: "LocationSettings");

            migrationBuilder.DropColumn(
                name: "LeadSeesAllSchedules",
                table: "LocationSettings");

            migrationBuilder.DropColumn(
                name: "ScheduleVisibilityEnabled",
                table: "LocationSettings");
        }
    }
}
