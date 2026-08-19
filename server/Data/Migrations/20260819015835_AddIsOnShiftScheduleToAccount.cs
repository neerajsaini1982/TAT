using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddIsOnShiftScheduleToAccount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing employees must default to true — this checkbox is meant to
            // be opted *out* of per issue #61 ("checked by default to existing and
            // new employees"), not silently drop everyone off the schedule.
            migrationBuilder.AddColumn<bool>(
                name: "IsOnShiftSchedule",
                table: "Accounts",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsOnShiftSchedule",
                table: "Accounts");
        }
    }
}
