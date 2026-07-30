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
    string? AdpStatus,
    decimal? HourlyRate,
    // Masked ("***-**-1234") — the real SSN never leaves the server.
    string? SsnMasked,
    string? DateOfBirth,
    string? HireDate,
    string? EmploymentType);

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
    int? LocationId,
    decimal? HourlyRate,
    // 9 digits, or null/empty to leave unset. Encrypted before storage.
    string? Ssn,
    DateOnly? DateOfBirth,
    DateOnly? HireDate,
    EmploymentType? EmploymentType);

public record UpdateAccountRequest(
    string FirstName,
    string LastName,
    string Email,
    string Phone,
    bool IsActive,
    AccountRole Role,
    // Only required when Role moves away from Employee (the account has no
    // real password on file yet — Employee accounts log in with a UserCode
    // and get a random, unknown PasswordHash at creation time).
    string? Username,
    string? Password,
    decimal? HourlyRate,
    // 9 digits to replace the stored SSN; null to leave it unchanged.
    string? Ssn,
    DateOnly? DateOfBirth,
    DateOnly? HireDate,
    EmploymentType? EmploymentType);

// LoginLink is built client-side (it already knows its own origin) and
// passed through rather than the server guessing its hostname.
public record SendCredentialsRequest(string LoginLink);
