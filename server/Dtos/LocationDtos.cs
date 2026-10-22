namespace Server.Dtos;

public record LocationDto(
    int Id,
    string Name,
    string Address,
    string LocationCode,
    string Phone,
    string Email,
    bool IsActive);

public record CreateLocationRequest(
    string Name,
    string Address,
    string LocationCode,
    string Phone,
    string Email);

public record UpdateLocationRequest(
    string Name,
    string Address,
    string Phone,
    string Email,
    bool IsActive);

public record ResetDemoLocationRequest(
    string AdminPassword,
    string KioskPasscode);

// UserCode/KioskPin are null for the Admin, who signs in with a username
// and password; Username is null for staff who don't have one.
public record DemoLoginDto(
    string Name,
    string JobTitle,
    string Role,
    string? Username,
    string? UserCode,
    string? KioskPin);

public record DemoLocationResult(
    LocationDto Location,
    IReadOnlyList<DemoLoginDto> Logins);
