namespace Server.Dtos;

public record AvailabilityDayDto(DateOnly Date, bool IsAvailable, TimeOnly? StartTime, TimeOnly? EndTime);

public record AvailabilityDto(
    int Id,
    int AccountId,
    string Username,
    string FirstName,
    string LastName,
    DateOnly WeekStartDate,
    bool IsSubmitted,
    DateTime? SubmittedAt,
    List<AvailabilityDayDto> Days);

public record SaveAvailabilityRequest(
    DateOnly WeekStartDate,
    List<AvailabilityDayDto> Days,
    bool Submit);
