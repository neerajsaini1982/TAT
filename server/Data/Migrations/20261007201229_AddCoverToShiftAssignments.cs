using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCoverToShiftAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CoverAssignedAt",
                table: "ShiftAssignments",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CoverAssignedByAccountId",
                table: "ShiftAssignments",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CoversAssignmentId",
                table: "ShiftAssignments",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OriginalShiftId",
                table: "ShiftAssignments",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShiftAssignments_CoverAssignedByAccountId",
                table: "ShiftAssignments",
                column: "CoverAssignedByAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftAssignments_CoversAssignmentId",
                table: "ShiftAssignments",
                column: "CoversAssignmentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShiftAssignments_OriginalShiftId",
                table: "ShiftAssignments",
                column: "OriginalShiftId");

            migrationBuilder.AddForeignKey(
                name: "FK_ShiftAssignments_Accounts_CoverAssignedByAccountId",
                table: "ShiftAssignments",
                column: "CoverAssignedByAccountId",
                principalTable: "Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ShiftAssignments_ShiftAssignments_CoversAssignmentId",
                table: "ShiftAssignments",
                column: "CoversAssignmentId",
                principalTable: "ShiftAssignments",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_ShiftAssignments_Shifts_OriginalShiftId",
                table: "ShiftAssignments",
                column: "OriginalShiftId",
                principalTable: "Shifts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ShiftAssignments_Accounts_CoverAssignedByAccountId",
                table: "ShiftAssignments");

            migrationBuilder.DropForeignKey(
                name: "FK_ShiftAssignments_ShiftAssignments_CoversAssignmentId",
                table: "ShiftAssignments");

            migrationBuilder.DropForeignKey(
                name: "FK_ShiftAssignments_Shifts_OriginalShiftId",
                table: "ShiftAssignments");

            migrationBuilder.DropIndex(
                name: "IX_ShiftAssignments_CoverAssignedByAccountId",
                table: "ShiftAssignments");

            migrationBuilder.DropIndex(
                name: "IX_ShiftAssignments_CoversAssignmentId",
                table: "ShiftAssignments");

            migrationBuilder.DropIndex(
                name: "IX_ShiftAssignments_OriginalShiftId",
                table: "ShiftAssignments");

            migrationBuilder.DropColumn(
                name: "CoverAssignedAt",
                table: "ShiftAssignments");

            migrationBuilder.DropColumn(
                name: "CoverAssignedByAccountId",
                table: "ShiftAssignments");

            migrationBuilder.DropColumn(
                name: "CoversAssignmentId",
                table: "ShiftAssignments");

            migrationBuilder.DropColumn(
                name: "OriginalShiftId",
                table: "ShiftAssignments");
        }
    }
}
