using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWriteUpAccountability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WriteUps_Accounts_AccountId",
                table: "WriteUps");

            migrationBuilder.AddColumn<DateTime>(
                name: "AcknowledgmentAt",
                table: "WriteUps",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AcknowledgmentStatus",
                table: "WriteUps",
                type: "TEXT",
                nullable: false,
                // Hand-set: EF defaults an enum-as-string column to "", which
                // can't be read back as an enum. Write-ups that already exist
                // predate acknowledgment, so they start Pending, and predate
                // types, so they're the step this feature was originally
                // called: a written write-up.
                defaultValue: "Pending");

            migrationBuilder.AddColumn<string>(
                name: "Type",
                table: "WriteUps",
                type: "TEXT",
                nullable: false,
                defaultValue: "Written");

            migrationBuilder.AddColumn<string>(
                name: "VoidReason",
                table: "WriteUps",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "VoidedAt",
                table: "WriteUps",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "WriteUpEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    WriteUpId = table.Column<int>(type: "INTEGER", nullable: false),
                    Action = table.Column<string>(type: "TEXT", nullable: false),
                    ByAccountId = table.Column<int>(type: "INTEGER", nullable: false),
                    At = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Detail = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WriteUpEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WriteUpEvents_Accounts_ByAccountId",
                        column: x => x.ByAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WriteUpEvents_WriteUps_WriteUpId",
                        column: x => x.WriteUpId,
                        principalTable: "WriteUps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WriteUpEvents_ByAccountId",
                table: "WriteUpEvents",
                column: "ByAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_WriteUpEvents_WriteUpId",
                table: "WriteUpEvents",
                column: "WriteUpId");

            migrationBuilder.AddForeignKey(
                name: "FK_WriteUps_Accounts_AccountId",
                table: "WriteUps",
                column: "AccountId",
                principalTable: "Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // Give write-ups that already exist a first audit entry, so their
            // history starts with who created them and when.
            migrationBuilder.Sql(
                """
                INSERT INTO "WriteUpEvents" ("WriteUpId", "Action", "ByAccountId", "At", "Detail")
                SELECT "Id", 'Created', "CreatedByAccountId", "CreatedAt", NULL FROM "WriteUps";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WriteUps_Accounts_AccountId",
                table: "WriteUps");

            migrationBuilder.DropTable(
                name: "WriteUpEvents");

            migrationBuilder.DropColumn(
                name: "AcknowledgmentAt",
                table: "WriteUps");

            migrationBuilder.DropColumn(
                name: "AcknowledgmentStatus",
                table: "WriteUps");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "WriteUps");

            migrationBuilder.DropColumn(
                name: "VoidReason",
                table: "WriteUps");

            migrationBuilder.DropColumn(
                name: "VoidedAt",
                table: "WriteUps");

            migrationBuilder.AddForeignKey(
                name: "FK_WriteUps_Accounts_AccountId",
                table: "WriteUps",
                column: "AccountId",
                principalTable: "Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
