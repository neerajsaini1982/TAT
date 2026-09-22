using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Server.Models;

namespace Server.Security;

public class TokenService(IConfiguration config)
{
    public const string LocationCodeClaimType = "locationCode";

    public string CreateToken(Account account, string? locationCode)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, account.Id.ToString()),
            new(ClaimTypes.Name, account.Username),
            new(ClaimTypes.Role, account.Role.ToString()),
        };

        if (locationCode is not null)
        {
            claims.Add(new Claim(LocationCodeClaimType, locationCode));
        }

        var jwtSection = config.GetSection("Jwt");
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSection["Key"]!));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expiryMinutes = jwtSection.GetValue<int>("ExpiryMinutes");

        var token = new JwtSecurityToken(
            issuer: jwtSection["Issuer"],
            audience: jwtSection["Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(expiryMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    // A kiosk session isn't any one Account — it's a shared device logged
    // into one location for a whole shift, so it gets a NameIdentifier
    // sentinel ("0", never a real account id) instead of an account-derived
    // one, and a much longer expiry (Jwt:KioskExpiryMinutes) than a normal
    // session so it doesn't need re-login between employee punches.
    public string CreateKioskToken(string locationCode)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "0"),
            new(ClaimTypes.Name, "kiosk"),
            new(ClaimTypes.Role, nameof(AccountRole.Kiosk)),
            new(LocationCodeClaimType, locationCode),
        };

        var jwtSection = config.GetSection("Jwt");
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSection["Key"]!));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expiryMinutes = jwtSection.GetValue<int>("KioskExpiryMinutes");

        var token = new JwtSecurityToken(
            issuer: jwtSection["Issuer"],
            audience: jwtSection["Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(expiryMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
