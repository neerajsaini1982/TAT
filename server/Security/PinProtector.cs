using Microsoft.AspNetCore.DataProtection;

namespace Server.Security;

// Encrypts a kiosk PIN for storage using ASP.NET Core Data Protection.
// Unlike SsnProtector, this one supports Unprotect — an Admin is allowed to
// look up an employee's existing PIN (not just reset it), so the PIN has to
// be recoverable, not just verifiable. Uses its own purpose string so it's
// cryptographically isolated from SsnProtector even though both ultimately
// share the same underlying key ring.
public class PinProtector(IDataProtectionProvider provider)
{
    private readonly IDataProtector protector = provider.CreateProtector("Server.Security.PinProtector.v1");

    public string Protect(string pin) => protector.Protect(pin);

    public string Unprotect(string encrypted) => protector.Unprotect(encrypted);
}
