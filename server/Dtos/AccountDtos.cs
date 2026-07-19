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
    string? LocationCode,
    // Populated by the ADP employee-directory import (see
    // EmployeeImportController); null for accounts created by hand.
    string? BirthDate,
    string? JobTitle,
    string? Address1,
    string? Address2,
    string? City,
    string? State,
    string? Zipcode,
    string? Supervisor,
    string? AdpStatus);

public record CreateAccountRequest(
    // Required unless Role is Employee — employees log in with a UserCode
    // instead and get a username/password generated for them.
    string? Username,
    string? Password,
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
