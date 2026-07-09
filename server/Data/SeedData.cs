using Server.Models;
using Server.Security;

namespace Server.Data;

public static class SeedData
{
    // Without this there would be no way to log in and create the first
    // real accounts. Sa has no location. Change this password immediately
    // in any environment other than local dev.
    public const string DefaultSaUsername = "sa";
    public const string DefaultSaPassword = "ChangeMe123!";

    public static void EnsureSaAccount(AppDbContext db)
    {
        if (db.Accounts.Any(a => a.Role == AccountRole.Sa))
        {
            return;
        }

        db.Accounts.Add(new Account
        {
            Username = DefaultSaUsername,
            PasswordHash = PasswordHasher.Hash(DefaultSaPassword),
            FirstName = "Super",
            LastName = "Admin",
            Email = "sa@example.com",
            Phone = string.Empty,
            Role = AccountRole.Sa,
            IsActive = true,
        });
        db.SaveChanges();

        Console.WriteLine($"Seeded default Sa account -> username: {DefaultSaUsername}, password: {DefaultSaPassword} (change this immediately outside local dev)");
    }
}
