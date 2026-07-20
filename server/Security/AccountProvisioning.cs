using System.Text.RegularExpressions;
using Server.Data;

namespace Server.Security;

// Shared by AccountsController (one account at a time) and
// EmployeeImportController (many at once from an ADP export) — both need to
// hand a brand-new Employee account a username and login code without ever
// colliding with an existing one.
public static class AccountProvisioning
{
    // Checks both the database and EF's local change tracker (Local), so a
    // bulk import that Adds many Accounts before its one SaveChanges() call
    // still gets distinct codes/usernames for every row — a plain db query
    // wouldn't see the earlier, not-yet-saved rows in the same batch.
    public static string GenerateUniqueUserCode(AppDbContext db, int locationId)
    {
        string code;
        do
        {
            code = Random.Shared.Next(0, 1_000_000).ToString("D6");
        } while (
            db.Accounts.Local.Any(a => a.LocationId == locationId && a.UserCode == code) ||
            db.Accounts.Any(a => a.LocationId == locationId && a.UserCode == code));

        return code;
    }

    // Employees don't pick a username, but Account.Username is still globally
    // unique, so derive one from their name and disambiguate if needed.
    public static string GenerateUniqueUsername(AppDbContext db, string firstName, string lastName)
    {
        var baseName = Regex.Replace($"{firstName}.{lastName}".ToLowerInvariant(), "[^a-z0-9.]", "").Trim('.');
        if (string.IsNullOrEmpty(baseName))
        {
            baseName = "employee";
        }

        var username = baseName;
        var suffix = 1;
        while (
            db.Accounts.Local.Any(a => a.Username == username) ||
            db.Accounts.Any(a => a.Username == username))
        {
            username = $"{baseName}{++suffix}";
        }

        return username;
    }
}
