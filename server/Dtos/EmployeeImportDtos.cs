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
