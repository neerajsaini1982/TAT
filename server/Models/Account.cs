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

    // Whether this account shows up as a row on the schedule screens (see
    // AvailabilityController.GetForLocation's onShiftScheduleOnly param).
    // Defaults true for everyone; an admin unchecks it for someone who
    // shouldn't be scheduled without deactivating their account entirely.
    public bool IsOnShiftSchedule { get; set; } = true;

    // Per-employee override of LocationSettings' role-based Schedule
    // Visibility setting (see ShiftAssignmentsController.CanSeeAllSchedules):
    // when true, this account sees every employee's shifts on "My Schedule"
    // regardless of their role's flag or whether ScheduleVisibilityEnabled
    // is even on for the location. Defaults false for everyone; an admin
    // opts a specific person in (e.g. a lead-in-training who isn't yet a
    // Lead). Does not affect clock-in/out, which stays self-service only
    // regardless of this flag (see TimeEntriesController.ClockIn).
    public bool CanSeeAllSchedules { get; set; } = false;
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

    public decimal? HourlyRate { get; set; }

    // Full DOB entered by hand, separate from the ADP-imported BirthDate
    // ("MM/DD", no year) which doubles as an import dedupe key and must not
    // change shape.
    public DateOnly? DateOfBirth { get; set; }

    // "Current" hire date, maintained directly on the account. Rehires keep
    // a full history in EmploymentPeriod instead of overwriting this.
    public DateOnly? HireDate { get; set; }

    public EmploymentType? EmploymentType { get; set; }

    // Ciphertext (ASP.NET Core Data Protection) and the plaintext last 4
    // digits used to render a mask ("***-**-1234") without decrypting. There
    // is no code path that decrypts SsnEncrypted back to plaintext.
    public string? SsnEncrypted { get; set; }
    public string? SsnLast4 { get; set; }

    // GUID-based name of the photo file on disk under PhotosRoot/{Id}/ —
    // never derived from user input. Null if no photo has been uploaded.
    public string? PhotoFileName { get; set; }
    public string? PhotoContentType { get; set; }
}
