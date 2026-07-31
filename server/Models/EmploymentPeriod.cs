namespace Server.Models;

// One stretch of employment for an account — an employee who leaves and
// comes back gets a second row rather than overwriting the first, so the
// full hire/rehire history is kept. Account.HireDate is the simple "current"
// value shown on the account form; this table is the audit trail, managed
// separately by an admin.
public class EmploymentPeriod
{
    public int Id { get; set; }

    public int AccountId { get; set; }
    public Account? Account { get; set; }

    public DateOnly HireDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
