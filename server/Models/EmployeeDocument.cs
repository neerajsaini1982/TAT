namespace Server.Models;

// An onboarding/HR document attached to an employee's account. The file
// itself lives on local disk under a GUID-based name (StoredFileName);
// everything else here is display metadata.
public class EmployeeDocument
{
    public int Id { get; set; }

    public int AccountId { get; set; }
    public Account? Account { get; set; }

    // Admin/employee-chosen label, e.g. "I-9", "Signed offer letter".
    public string Name { get; set; } = string.Empty;

    // Original filename as uploaded, kept only for display/download.
    public string FileName { get; set; } = string.Empty;

    // GUID-based name of the file on disk under DocumentsRoot/{AccountId}/ —
    // never derived from user input, to avoid path traversal or collisions.
    public string StoredFileName { get; set; } = string.Empty;

    public string ContentType { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }

    public int UploadedByAccountId { get; set; }
    public Account? UploadedByAccount { get; set; }

    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
}
