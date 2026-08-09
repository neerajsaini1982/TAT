using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOvertimeThresholdToLocationSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "OvertimeDailyThresholdMinutes",
                table: "LocationSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 480);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OvertimeDailyThresholdMinutes",
                table: "LocationSettings");
        }
    }
}
