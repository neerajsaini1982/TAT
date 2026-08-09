using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddClockInAnywhereAndAllowedPunchDevices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Defaults on (per LocationSettings' C# default) so existing
            // rows come out of this migration behaving exactly like new
            // ones do — see AddScheduleVisibilityToLocationSettings for the
            // same pattern.
            migrationBuilder.AddColumn<bool>(
                name: "ClockInAnywhere",
                table: "LocationSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "AllowedPunchDevices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    LocationId = table.Column<int>(type: "INTEGER", nullable: false),
                    IpAddress = table.Column<string>(type: "TEXT", nullable: false),
                    Label = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AllowedPunchDevices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AllowedPunchDevices_Locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AllowedPunchDevices_LocationId_IpAddress",
                table: "AllowedPunchDevices",
                columns: new[] { "LocationId", "IpAddress" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AllowedPunchDevices");

            migrationBuilder.DropColumn(
                name: "ClockInAnywhere",
                table: "LocationSettings");
        }
    }
}
