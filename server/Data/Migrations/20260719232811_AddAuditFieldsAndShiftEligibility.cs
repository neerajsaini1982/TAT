using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditFieldsAndShiftEligibility : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "EditedAt",
                table: "TimeEntries",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EditedByAccountId",
                table: "TimeEntries",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AbsentMarkedAt",
                table: "ShiftAssignments",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AbsentMarkedByAccountId",
                table: "ShiftAssignments",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TimeEntries_EditedByAccountId",
                table: "TimeEntries",
                column: "EditedByAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftAssignments_AbsentMarkedByAccountId",
                table: "ShiftAssignments",
                column: "AbsentMarkedByAccountId");

            migrationBuilder.AddForeignKey(
                name: "FK_ShiftAssignments_Accounts_AbsentMarkedByAccountId",
                table: "ShiftAssignments",
                column: "AbsentMarkedByAccountId",
                principalTable: "Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TimeEntries_Accounts_EditedByAccountId",
                table: "TimeEntries",
                column: "EditedByAccountId",
                principalTable: "Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ShiftAssignments_Accounts_AbsentMarkedByAccountId",
                table: "ShiftAssignments");

            migrationBuilder.DropForeignKey(
                name: "FK_TimeEntries_Accounts_EditedByAccountId",
                table: "TimeEntries");

            migrationBuilder.DropIndex(
                name: "IX_TimeEntries_EditedByAccountId",
                table: "TimeEntries");

            migrationBuilder.DropIndex(
                name: "IX_ShiftAssignments_AbsentMarkedByAccountId",
                table: "ShiftAssignments");

            migrationBuilder.DropColumn(
                name: "EditedAt",
                table: "TimeEntries");

            migrationBuilder.DropColumn(
                name: "EditedByAccountId",
                table: "TimeEntries");

            migrationBuilder.DropColumn(
                name: "AbsentMarkedAt",
                table: "ShiftAssignments");

            migrationBuilder.DropColumn(
                name: "AbsentMarkedByAccountId",
                table: "ShiftAssignments");
        }
    }
}
