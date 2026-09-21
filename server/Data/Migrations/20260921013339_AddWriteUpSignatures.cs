using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWriteUpSignatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SignatureId",
                table: "WriteUpEvents",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "WriteUpSignatures",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    WriteUpId = table.Column<int>(type: "INTEGER", nullable: false),
                    SignedName = table.Column<string>(type: "TEXT", nullable: false),
                    SignedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Png = table.Column<byte[]>(type: "BLOB", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WriteUpSignatures", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WriteUpSignatures_WriteUps_WriteUpId",
                        column: x => x.WriteUpId,
                        principalTable: "WriteUps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WriteUpEvents_SignatureId",
                table: "WriteUpEvents",
                column: "SignatureId");

            migrationBuilder.CreateIndex(
                name: "IX_WriteUpSignatures_WriteUpId",
                table: "WriteUpSignatures",
                column: "WriteUpId");

            migrationBuilder.AddForeignKey(
                name: "FK_WriteUpEvents_WriteUpSignatures_SignatureId",
                table: "WriteUpEvents",
                column: "SignatureId",
                principalTable: "WriteUpSignatures",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WriteUpEvents_WriteUpSignatures_SignatureId",
                table: "WriteUpEvents");

            migrationBuilder.DropTable(
                name: "WriteUpSignatures");

            migrationBuilder.DropIndex(
                name: "IX_WriteUpEvents_SignatureId",
                table: "WriteUpEvents");

            migrationBuilder.DropColumn(
                name: "SignatureId",
                table: "WriteUpEvents");
        }
    }
}
