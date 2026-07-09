using Server.Models;

namespace Server.Dtos;

public record AccountDto(
    int Id,
    string Username,
    string FirstName,
    string LastName,
    string Email,
    string Phone,
    string Role,
    bool IsActive,
    string? UserCode,
    string? LocationCode);

public record CreateAccountRequest(
    string Username,
    string Password,
    string FirstName,
    string LastName,
    string Email,
    string Phone,
    AccountRole Role,
    // Ignored for callers with the Admin role; they are always scoped to
    // their own location. Required for Sa when Role is not Sa.
    int? LocationId);

public record UpdateAccountRequest(
    string FirstName,
    string LastName,
    string Email,
    string Phone,
    bool IsActive);
