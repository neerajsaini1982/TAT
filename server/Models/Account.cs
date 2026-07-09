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
}
