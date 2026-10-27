namespace Server.Models;

public enum TimeOffStatus
{
    Pending,
    Approved,
    Denied,
    Cancelled,
}

// An employee's request to be away for one day or a range of days. Created
// Pending by the employee, or straight to Approved when an admin enters it
// on their behalf. Never deleted — every change is in Events.
public class TimeOffRequest
{
    public int Id { get; set; }

    public int AccountId { get; set; }
    public Account? Account { get; set; }

    // Copied from the account when created, for filtering by location.
    public int LocationId { get; set; }

    public int TimeOffTypeId { get; set; }
    public TimeOffType? TimeOffType { get; set; }

    // Inclusive range, in the location's time zone.
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }

    // Only allowed for a single day; PartialMinutes is then required.
    public bool IsPartialDay { get; set; }
    public int? PartialMinutes { get; set; }

    public string? Note { get; set; }

    public TimeOffStatus Status { get; set; } = TimeOffStatus.Pending;

    // Copied from the type when approved; null until then.
    public bool? IsPaid { get; set; }

    public int? ReviewedByAccountId { get; set; }
    public Account? ReviewedByAccount { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewComment { get; set; }

    public int CreatedByAccountId { get; set; }
    public Account? CreatedByAccount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Hours each day counts for, fixed when the request is approved (see
    // TimeOffRequestsController.Approve). Empty until then. The payroll
    // report reads these, so removing a shift afterwards doesn't change pay.
    public List<TimeOffRequestDay> Days { get; set; } = [];

    public List<TimeOffRequestEvent> Events { get; set; } = [];
}

public class TimeOffRequestDay
{
    public int Id { get; set; }

    public int TimeOffRequestId { get; set; }
    public TimeOffRequest? TimeOffRequest { get; set; }

    public DateOnly Date { get; set; }
    public int Minutes { get; set; }
}
