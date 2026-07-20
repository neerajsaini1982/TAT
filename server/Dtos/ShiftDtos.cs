using Server.Models;

namespace Server.Dtos;

public record ScheduledBreakDto(BreakKind Kind, TimeOnly StartTime, TimeOnly EndTime);

public record ShiftDto(
    int Id,
    string Name,
    TimeOnly StartTime,
    TimeOnly EndTime,
    List<ScheduledBreakDto> ScheduledBreaks,
    bool IsActive,
    string LocationCode);

public record CreateShiftRequest(
    string Name,
    TimeOnly StartTime,
    TimeOnly EndTime,
    List<ScheduledBreakDto> ScheduledBreaks,
    // Ignored for callers with the Admin role; they are always scoped to
    // their own location. Required for Sa.
    int? LocationId);

public record UpdateShiftRequest(
    string Name,
    TimeOnly StartTime,
    TimeOnly EndTime,
    List<ScheduledBreakDto> ScheduledBreaks,
    bool IsActive);
