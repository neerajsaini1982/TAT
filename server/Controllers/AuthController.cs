using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Server.Data;
using Server.Dtos;
using Server.Models;
using Server.Security;

namespace Server.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(AppDbContext db, TokenService tokens) : ControllerBase
{
    [HttpPost("sa-login")]
    public ActionResult<AuthResponse> SaLogin(SaLoginRequest request)
    {
        var account = db.Accounts.SingleOrDefault(a =>
            a.Username == request.Username && a.Role == AccountRole.Sa);

        if (account is null || !account.IsActive || !PasswordHasher.Verify(request.Password, account.PasswordHash))
        {
            return Unauthorized();
        }

        var token = tokens.CreateToken(account, locationCode: null);
        return Ok(new AuthResponse(token, account.Id, account.Username, account.FirstName, account.LastName, account.Role.ToString(), null, null));
    }

    [HttpPost("admin-login")]
    public ActionResult<AuthResponse> AdminLogin(AdminLoginRequest request)
    {
        var account = db.Accounts
            .Include(a => a.Location)
            .SingleOrDefault(a =>
                a.Username == request.Username &&
                (a.Role == AccountRole.Admin || a.Role == AccountRole.Lead));

        if (account is null || !account.IsActive || account.Location is null ||
            !string.Equals(account.Location.LocationCode, request.LocationCode, StringComparison.OrdinalIgnoreCase) ||
            !PasswordHasher.Verify(request.Password, account.PasswordHash))
        {
            return Unauthorized();
        }

        var token = tokens.CreateToken(account, account.Location.LocationCode);
        return Ok(new AuthResponse(token, account.Id, account.Username, account.FirstName, account.LastName, account.Role.ToString(), account.Location.LocationCode, account.Location.Name));
    }

    [HttpPost("employee-login")]
    public ActionResult<AuthResponse> EmployeeLogin(EmployeeLoginRequest request)
    {
        var account = db.Accounts
            .Include(a => a.Location)
            .SingleOrDefault(a =>
                a.UserCode == request.UserCode &&
                a.Location != null &&
                a.Location.LocationCode == request.LocationCode);

        if (account is null || !account.IsActive || account.Location is null)
        {
            return Unauthorized();
        }

        var token = tokens.CreateToken(account, account.Location.LocationCode);
        return Ok(new AuthResponse(token, account.Id, account.Username, account.FirstName, account.LastName, account.Role.ToString(), account.Location.LocationCode, account.Location.Name));
    }

    // Logs in the shared kiosk device itself, not any one employee — see
    // TokenService.CreateKioskToken. Individual employees still identify
    // themselves per-punch with their own PIN (KioskController).
    [HttpPost("kiosk-login")]
    public ActionResult<AuthResponse> KioskLogin(KioskLoginRequest request)
    {
        var location = db.Locations.SingleOrDefault(l => l.LocationCode == request.LocationCode);
        var settings = location is null ? null : db.LocationSettings.SingleOrDefault(s => s.LocationId == location.Id);

        if (location is null || settings?.KioskPasscodeHash is null ||
            !PasswordHasher.Verify(request.Passcode, settings.KioskPasscodeHash))
        {
            return Unauthorized();
        }

        var token = tokens.CreateKioskToken(location.LocationCode);
        return Ok(new AuthResponse(token, 0, "kiosk", location.Name, string.Empty, nameof(AccountRole.Kiosk), location.LocationCode, location.Name));
    }
}
