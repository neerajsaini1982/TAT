namespace Server.Dtos;

public record SickTimeEntryDto(
    int Id,
    int AccountId,
    string AccountFirstName,
    string AccountLastName,
    DateOnly Date,
    int Minutes,
    string? Note,
    int RecordedByAccountId,
    DateTime RecordedAt);

public record CreateSickTimeEntryRequest(
    int AccountId,
    DateOnly Date,
    int Minutes,
    string? Note);
