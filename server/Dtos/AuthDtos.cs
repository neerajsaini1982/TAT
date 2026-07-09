namespace Server.Dtos;

public record SaLoginRequest(string Username, string Password);

public record AdminLoginRequest(string LocationCode, string Username, string Password);

public record EmployeeLoginRequest(string LocationCode, string UserCode);

public record AuthResponse(
    string Token,
    string Username,
    string FirstName,
    string LastName,
    string Role,
    string? LocationCode,
    string? LocationName);
