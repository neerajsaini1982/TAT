namespace Server.Models;

public class Account
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public AccountRole Role { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // 6-digit code an Employee types in at /{locationCode}/employee to log in.
    // Unique within a location; null for Sa accounts (they have no location).
    public string? UserCode { get; set; }

    // Null only for Sa accounts. Every Admin/Lead/Employee belongs to exactly one location.
    public int? LocationId { get; set; }
    public Location? Location { get; set; }

    // Populated by the ADP employee-directory import (see
    // EmployeeImportController); null for accounts created by hand. Stored
    // as ADP gives it, "MM/DD" with no year — combined with FirstName/
    // LastName it's the dedupe key that stops a re-upload from creating
    // duplicates.
    public string? BirthDate { get; set; }
    public string? JobTitle { get; set; }
    public string? Address1 { get; set; }
    public string? Address2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Zipcode { get; set; }
    public string? Supervisor { get; set; }

    // Raw ADP Status string ("Active"/"Terminated"), kept alongside IsActive
    // even though they mean the same thing today.
    public string? AdpStatus { get; set; }
}
