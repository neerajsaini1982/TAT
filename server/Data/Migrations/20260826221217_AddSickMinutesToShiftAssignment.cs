using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSickMinutesToShiftAssignment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "SickHoursRecordedAt",
                table: "ShiftAssignments",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SickHoursRecordedByAccountId",
                table: "ShiftAssignments",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SickMinutes",
                table: "ShiftAssignments",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_ShiftAssignments_SickHoursRecordedByAccountId",
                table: "ShiftAssignments",
                column: "SickHoursRecordedByAccountId");

            migrationBuilder.AddForeignKey(
                name: "FK_ShiftAssignments_Accounts_SickHoursRecordedByAccountId",
                table: "ShiftAssignments",
                column: "SickHoursRecordedByAccountId",
                principalTable: "Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ShiftAssignments_Accounts_SickHoursRecordedByAccountId",
                table: "ShiftAssignments");

            migrationBuilder.DropIndex(
                name: "IX_ShiftAssignments_SickHoursRecordedByAccountId",
                table: "ShiftAssignments");

            migrationBuilder.DropColumn(
                name: "SickHoursRecordedAt",
                table: "ShiftAssignments");

            migrationBuilder.DropColumn(
                name: "SickHoursRecordedByAccountId",
                table: "ShiftAssignments");

            migrationBuilder.DropColumn(
                name: "SickMinutes",
                table: "ShiftAssignments");
        }
    }
}
