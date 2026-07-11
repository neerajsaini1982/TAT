using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBreakAndLunchToTimeEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
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
        }
    }
}
