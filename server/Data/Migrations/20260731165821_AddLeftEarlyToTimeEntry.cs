using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLeftEarlyToTimeEntry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "LeftEarly",
                table: "TimeEntries",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "LeftEarlyMarkedAt",
                table: "TimeEntries",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LeftEarlyMarkedByAccountId",
                table: "TimeEntries",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LeftEarlyNote",
                table: "TimeEntries",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TimeEntries_LeftEarlyMarkedByAccountId",
                table: "TimeEntries",
                column: "LeftEarlyMarkedByAccountId");

            migrationBuilder.AddForeignKey(
                name: "FK_TimeEntries_Accounts_LeftEarlyMarkedByAccountId",
                table: "TimeEntries",
                column: "LeftEarlyMarkedByAccountId",
                principalTable: "Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TimeEntries_Accounts_LeftEarlyMarkedByAccountId",
                table: "TimeEntries");

            migrationBuilder.DropIndex(
                name: "IX_TimeEntries_LeftEarlyMarkedByAccountId",
                table: "TimeEntries");

            migrationBuilder.DropColumn(
                name: "LeftEarly",
                table: "TimeEntries");

            migrationBuilder.DropColumn(
                name: "LeftEarlyMarkedAt",
                table: "TimeEntries");

            migrationBuilder.DropColumn(
                name: "LeftEarlyMarkedByAccountId",
                table: "TimeEntries");

            migrationBuilder.DropColumn(
                name: "LeftEarlyNote",
                table: "TimeEntries");
        }
    }
}
