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

public record CopyPreviousWeekResult(int Copied, int Skipped);

// See AvailabilityController.SendReminder. AvailabilityLink fills the
// template's {{availabilityLink}} — the client knows its own origin, same as
// SendCredentialsRequest.LoginLink.
public record SendAvailabilityReminderRequest(string? LocationCode, DateOnly WeekStartDate, string AvailabilityLink);

// Full names in each bucket; AlreadySubmitted is just a count, since those
// people were never going to be emailed.
public record AvailabilityReminderResult(List<string> Sent, List<string> SkippedNoEmail, List<string> Failed, int AlreadySubmitted);
