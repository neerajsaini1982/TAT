using System.Security.Cryptography;
using System.Text;
using Server.Data;
using Server.Models;

namespace Server.Security;

public record PinVerifyResult(bool Ok, string? Error, Account? Account);

// Verifies the short PIN an employee types at a kiosk device to punch
// themselves in/out (see KioskController), independent of the kiosk
// device's own session (a KioskLogin passcode — see AuthController). Tracks
// failed attempts per-account and locks punching for that employee for 15
// minutes after 5 wrong PINs in a row, clearable early by an Admin/Lead
// (see AccountsController.ClearKioskPinLock).
public class KioskPinService(AppDbContext db, PinProtector pinProtector)
{
    private const int MaxAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    public PinVerifyResult VerifyAndConsume(int accountId, string locationCode, string pin)
    {
        var account = db.Accounts.SingleOrDefault(a =>
            a.Id == accountId &&
            a.IsActive &&
            a.Location != null &&
            a.Location.LocationCode == locationCode);

        if (account is null || account.PinEncrypted is null)
        {
            return new PinVerifyResult(false, "PIN not set up for this employee.", null);
        }

        if (account.PinLockedUntil is { } until && until > DateTime.UtcNow)
        {
            return new PinVerifyResult(false, $"Too many attempts. Try again after {until.ToLocalTime():h:mm tt}.", null);
        }

        // Constant-time compare, same rigor as PasswordHasher.Verify, even
        // though the PIN is recoverable rather than one-way hashed — no
        // reason to leak timing info about how many leading digits matched.
        var actual = Encoding.UTF8.GetBytes(pinProtector.Unprotect(account.PinEncrypted));
        var submitted = Encoding.UTF8.GetBytes(pin);
        var matches = actual.Length == submitted.Length && CryptographicOperations.FixedTimeEquals(actual, submitted);
        if (!matches)
        {
            account.PinFailedAttempts++;
            if (account.PinFailedAttempts >= MaxAttempts)
            {
                account.PinLockedUntil = DateTime.UtcNow.Add(LockoutDuration);
            }
            db.SaveChanges();

            return new PinVerifyResult(false, "Incorrect PIN.", null);
        }

        account.PinFailedAttempts = 0;
        account.PinLockedUntil = null;
        db.SaveChanges();

        return new PinVerifyResult(true, null, account);
    }
}
