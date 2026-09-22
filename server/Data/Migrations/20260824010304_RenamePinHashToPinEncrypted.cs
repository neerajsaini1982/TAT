using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class RenamePinHashToPinEncrypted : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "PinHash",
                table: "Accounts",
                newName: "PinEncrypted");

            // Any PIN set before this migration was PasswordHasher-hashed,
            // not PinProtector-encrypted — those values can't be decrypted
            // under the new scheme, so clear them rather than leave garbage
            // an Admin's PIN lookup would crash on. Affected employees just
            // need their kiosk PIN set again.
            migrationBuilder.Sql("UPDATE \"Accounts\" SET \"PinEncrypted\" = NULL WHERE \"PinEncrypted\" IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "PinEncrypted",
                table: "Accounts",
                newName: "PinHash");
        }
    }
}
