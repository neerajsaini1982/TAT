namespace Server.Dtos;

public record EmployeeDocumentDto(
    int Id,
    int AccountId,
    string Name,
    string FileName,
    string ContentType,
    long FileSizeBytes,
    string UploadedAt,
    int UploadedByAccountId,
    string UploadedByName);

public record UpdateEmployeeDocumentRequest(string Name);
