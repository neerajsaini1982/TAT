using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTimeOffRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "TimeOffEnabled",
                table: "LocationSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "TimeOffFullDayMinutes",
                table: "LocationSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 480);

            migrationBuilder.AddColumn<int>(
                name: "TimeOffMinNoticeDays",
                table: "LocationSettings",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TimeOffBlackouts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    LocationId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TimeOffBlackouts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TimeOffBlackouts_Locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TimeOffTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    LocationId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    IsPaid = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    Color = table.Column<string>(type: "TEXT", nullable: true),
                    PayrollCode = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TimeOffTypes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TimeOffTypes_Locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TimeOffRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AccountId = table.Column<int>(type: "INTEGER", nullable: false),
                    LocationId = table.Column<int>(type: "INTEGER", nullable: false),
                    TimeOffTypeId = table.Column<int>(type: "INTEGER", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    IsPartialDay = table.Column<bool>(type: "INTEGER", nullable: false),
                    PartialMinutes = table.Column<int>(type: "INTEGER", nullable: true),
                    Note = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    IsPaid = table.Column<bool>(type: "INTEGER", nullable: true),
                    ReviewedByAccountId = table.Column<int>(type: "INTEGER", nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ReviewComment = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedByAccountId = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TimeOffRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TimeOffRequests_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TimeOffRequests_Accounts_CreatedByAccountId",
                        column: x => x.CreatedByAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TimeOffRequests_Accounts_ReviewedByAccountId",
                        column: x => x.ReviewedByAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TimeOffRequests_TimeOffTypes_TimeOffTypeId",
                        column: x => x.TimeOffTypeId,
                        principalTable: "TimeOffTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TimeOffRequestDays",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TimeOffRequestId = table.Column<int>(type: "INTEGER", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Minutes = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TimeOffRequestDays", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TimeOffRequestDays_TimeOffRequests_TimeOffRequestId",
                        column: x => x.TimeOffRequestId,
                        principalTable: "TimeOffRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TimeOffRequestEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TimeOffRequestId = table.Column<int>(type: "INTEGER", nullable: false),
                    Action = table.Column<string>(type: "TEXT", nullable: false),
                    ActorAccountId = table.Column<int>(type: "INTEGER", nullable: false),
                    At = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Comment = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TimeOffRequestEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TimeOffRequestEvents_Accounts_ActorAccountId",
                        column: x => x.ActorAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TimeOffRequestEvents_TimeOffRequests_TimeOffRequestId",
                        column: x => x.TimeOffRequestId,
                        principalTable: "TimeOffRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TimeOffBlackouts_LocationId_StartDate",
                table: "TimeOffBlackouts",
                columns: new[] { "LocationId", "StartDate" });

            migrationBuilder.CreateIndex(
                name: "IX_TimeOffRequestDays_TimeOffRequestId_Date",
                table: "TimeOffRequestDays",
                columns: new[] { "TimeOffRequestId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TimeOffRequestEvents_ActorAccountId",
                table: "TimeOffRequestEvents",
                column: "ActorAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_TimeOffRequestEvents_TimeOffRequestId",
                table: "TimeOffRequestEvents",
                column: "TimeOffRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_TimeOffRequests_AccountId_StartDate",
                table: "TimeOffRequests",
                columns: new[] { "AccountId", "StartDate" });

            migrationBuilder.CreateIndex(
                name: "IX_TimeOffRequests_CreatedByAccountId",
                table: "TimeOffRequests",
                column: "CreatedByAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_TimeOffRequests_LocationId_Status",
                table: "TimeOffRequests",
                columns: new[] { "LocationId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_TimeOffRequests_ReviewedByAccountId",
                table: "TimeOffRequests",
                column: "ReviewedByAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_TimeOffRequests_TimeOffTypeId",
                table: "TimeOffRequests",
                column: "TimeOffTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_TimeOffTypes_LocationId_Name",
                table: "TimeOffTypes",
                columns: new[] { "LocationId", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TimeOffBlackouts");

            migrationBuilder.DropTable(
                name: "TimeOffRequestDays");

            migrationBuilder.DropTable(
                name: "TimeOffRequestEvents");

            migrationBuilder.DropTable(
                name: "TimeOffRequests");

            migrationBuilder.DropTable(
                name: "TimeOffTypes");

            migrationBuilder.DropColumn(
                name: "TimeOffEnabled",
                table: "LocationSettings");

            migrationBuilder.DropColumn(
                name: "TimeOffFullDayMinutes",
                table: "LocationSettings");

            migrationBuilder.DropColumn(
                name: "TimeOffMinNoticeDays",
                table: "LocationSettings");
        }
    }
}
