namespace Server.Dtos;

public record ShiftDto(
    int Id,
    string Name,
    TimeOnly StartTime,
    TimeOnly EndTime,
    bool IsBreakRequired,
    bool IsLunchRequired,
    bool IsActive,
    string LocationCode);

public record CreateShiftRequest(
    string Name,
    TimeOnly StartTime,
    TimeOnly EndTime,
    bool IsBreakRequired,
    bool IsLunchRequired,
    // Ignored for callers with the Admin role; they are always scoped to
    // their own location. Required for Sa.
    int? LocationId);

public record UpdateShiftRequest(
    string Name,
    TimeOnly StartTime,
    TimeOnly EndTime,
    bool IsBreakRequired,
    bool IsLunchRequired,
    bool IsActive);
