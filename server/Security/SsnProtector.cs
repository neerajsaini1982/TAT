using Microsoft.AspNetCore.DataProtection;

namespace Server.Security;

// Encrypts an SSN for storage using ASP.NET Core Data Protection. There is
// deliberately no Unprotect method — nothing in this app ever needs the
// plaintext SSN back, only the last-4 digits (stored separately, in the
// clear) for display as a mask.
public class SsnProtector(IDataProtectionProvider provider)
{
    private readonly IDataProtector protector = provider.CreateProtector("Server.Security.SsnProtector.v1");

    public string Protect(string digits) => protector.Protect(digits);
}
