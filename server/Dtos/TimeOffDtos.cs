using Server.Models;

namespace Server.Dtos;

public record TimeOffTypeDto(
    int Id, string Name, bool IsPaid, bool IsActive, int SortOrder, string? Color, string? PayrollCode, bool IsUsed);

public record SaveTimeOffTypeRequest(
    string Name, bool IsPaid, bool IsActive, int SortOrder, string? Color, string? PayrollCode);

public record TimeOffBlackoutDto(int Id, string Name, DateOnly StartDate, DateOnly EndDate);

public record SaveTimeOffBlackoutRequest(string Name, DateOnly StartDate, DateOnly EndDate);

public record TimeOffSettingsDto(bool TimeOffEnabled, int? TimeOffMinNoticeDays, int TimeOffFullDayMinutes);

public record TimeOffRequestDayDto(DateOnly Date, int Minutes);

public record TimeOffRequestEventDto(TimeOffEventAction Action, string ActorName, DateTime At, string? Comment);

// History is only filled in for admins; an employee never sees it.
public record TimeOffRequestDto(
    int Id,
    int AccountId,
    string EmployeeName,
    int TimeOffTypeId,
    string TypeName,
    string? TypeColor,
    DateOnly StartDate,
    DateOnly EndDate,
    bool IsPartialDay,
    int? PartialMinutes,
    string? Note,
    TimeOffStatus Status,
    bool? IsPaid,
    string? ReviewedByName,
    DateTime? ReviewedAt,
    string? ReviewComment,
    DateTime CreatedAt,
    int TotalMinutes,
    List<TimeOffRequestDayDto> Days,
    List<TimeOffRequestEventDto>? History);

public record SubmitTimeOffRequest(
    int TimeOffTypeId, DateOnly StartDate, DateOnly EndDate, bool IsPartialDay, int? PartialMinutes, string? Note);

// Admin entering time off for someone (already approved). Days optionally
// overrides how many minutes each day counts for, same as on approve.
public record AdminCreateTimeOffRequest(
    int AccountId,
    int TimeOffTypeId,
    DateOnly StartDate,
    DateOnly EndDate,
    bool IsPartialDay,
    int? PartialMinutes,
    string? Note,
    bool RemoveConflictingAssignments,
    List<TimeOffRequestDayDto>? Days);

public record ApproveTimeOffRequest(string? Comment, bool RemoveConflictingAssignments, List<TimeOffRequestDayDto>? Days);

public record TimeOffCommentRequest(string? Comment);

// What approving would mean: the shifts already in the range, and the
// suggested minutes per day (shift length, or the location's full-day length).
public record TimeOffConflictDto(int ShiftAssignmentId, DateOnly Date, string ShiftName, TimeOnly StartTime, TimeOnly EndTime, bool IsPublished);

public record TimeOffPreviewDto(List<TimeOffConflictDto> Conflicts, List<TimeOffRequestDayDto> SuggestedDays);

// Approved time off for schedule views.
public record TimeOffCalendarEntryDto(
    int RequestId, int AccountId, string EmployeeName, DateOnly Date, string TypeName, string? TypeColor, bool IsPartialDay, int Minutes);
