namespace Server.Models;

// One kind of time off a location offers (Vacation, Bereavement, ...). Each
// location keeps its own list, managed by its admin; the defaults are
// created on first use (see TimeOffDefaults) and all start unpaid. Sick time
// is deliberately not one of these — it keeps its own always-paid path
// (ShiftAssignment.SickMinutes / SickTimeEntry).
public class TimeOffType
{
    public int Id { get; set; }

    public int LocationId { get; set; }
    public Location? Location { get; set; }

    public string Name { get; set; } = string.Empty;

    // Copied onto a request when it's approved (TimeOffRequest.IsPaid), so
    // changing this later never rewrites past pay periods.
    public bool IsPaid { get; set; }

    // Inactive types can't be picked for new requests. A type that has been
    // used is deactivated rather than deleted, so old requests keep their label.
    public bool IsActive { get; set; } = true;

    public int SortOrder { get; set; }

    // Optional hex colour for the schedule, e.g. "#4f8a5b".
    public string? Color { get; set; }

    // Optional payroll earnings code (e.g. ADP) for a future export.
    public string? PayrollCode { get; set; }
}
