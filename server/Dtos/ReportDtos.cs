namespace Server.Dtos;

// One calendar day within an employee's hours report — either a worked day
// (WorkedMinutes set), an absence (IsAbsent, from ShiftAssignment.IsAbsent),
// or neither if the employee was scheduled but hasn't clocked in yet and
// hasn't been marked absent. StillClockedIn covers an entry with no
// ClockOutAt yet, where worked/net time can't be computed.
public record DailyHoursDto(
    DateOnly Date,
    int? WorkedMinutes,
    int BreakMinutes,
    int LunchMinutes,
    int? NetWorkedMinutes,
    bool IsAbsent,
    string? AbsenceNote,
    bool StillClockedIn,
    bool HasLongBreak,
    bool HasLongLunch,
    List<string> Notes);

// Consolidated totals for one employee across the requested date range — the
// top level of the drill-down report (see ReportsController). Days is the
// per-date breakdown an admin expands into.
public record EmployeeHoursReportDto(
    int EmployeeId,
    string FullName,
    int TotalWorkedMinutes,
    int TotalBreakMinutes,
    int TotalLunchMinutes,
    int TotalNetWorkedMinutes,
    int AbsentDays,
    List<DailyHoursDto> Days);
