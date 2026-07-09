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
        return Ok(new AuthResponse(token, account.Username, account.FirstName, account.LastName, account.Role.ToString(), null));
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
        return Ok(new AuthResponse(token, account.Username, account.FirstName, account.LastName, account.Role.ToString(), account.Location.LocationCode));
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
        return Ok(new AuthResponse(token, account.Username, account.FirstName, account.LastName, account.Role.ToString(), account.Location.LocationCode));
    }
}
