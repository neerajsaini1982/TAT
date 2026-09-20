namespace Server.Models;

public enum AccountRole
{
    Sa,
    Admin,
    Lead,
    Employee,

    // Not a real account — a shared kiosk device's own JWT carries this role
    // (see TokenService.CreateKioskToken). No Account row ever has this
    // value.
    Kiosk,
}
