namespace Server.Dtos;

// One row parsed from an ADP Employee Directory export. WillCreate/SkipReason
// are set by EmployeeImportController before either step returns it — never
// true for a row matching an existing account (see the dedupe key in
// EmployeeImportController.DedupeKey).
public record EmployeeImportRowDto(
    string FirstName,
    string LastName,
    string? BirthDate,
    string? JobTitle,
    string? Address1,
    string? Address2,
    string? City,
    string? State,
    string? Zipcode,
    string? Phone,
    string? Supervisor,
    string? AdpStatus,
    bool IsActive,
    // Optional columns, present only if the workbook has a matching header
    // ("Hourly Rate"/"Hire Date"/"Employment Type") — null otherwise, never
    // a parse failure. EmploymentType stays a raw string through preview
    // (mirrors AdpStatus) and is parsed leniently into the enum at commit.
    decimal? HourlyRate,
    string? HireDate,
    string? EmploymentType,
    bool WillCreate,
    string? SkipReason);

public record EmployeeImportPreviewResult(
    List<EmployeeImportRowDto> Rows,
    int TotalRows,
    int NewCount,
    int SkippedCount);

// The rows the admin confirmed from the preview (typically every WillCreate
// row, minus any they unchecked) — the file itself isn't re-uploaded here.
public record EmployeeImportCommitRequest(
    string? LocationCode,
    List<EmployeeImportRowDto> Rows);

public record EmployeeImportCommitResult(
    int CreatedCount,
    int SkippedCount,
    List<AccountDto> Created);
