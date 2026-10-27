using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Server.Data;
using Server.Dtos;
using Server.Hubs;
using Server.Models;
using Server.Security;
using Server.Services;

namespace Server.Controllers;

// Time-off requests (issue #100, docs/tickets/time-off-requests.md).
// Employees ask for days off; only the location's admin (AdminOrAbove — not
// leads) approves, denies or enters time off for someone. Employees can
// cancel their own requests, approved ones included, as long as the time
// off hasn't started. Every change is kept in TimeOffRequestEvent.
[ApiController]
[Route("api/time-off-requests")]
[Authorize]
public class TimeOffRequestsController(AppDbContext db, IScheduleNotifier notifier, IEmailSender emailSender) : ControllerBase
{
    // Longest single request, so a typo in the year can't book 300 days.
    private const int MaxRangeDays = 62;

    [HttpGet("mine")]
    public ActionResult<IEnumerable<TimeOffRequestDto>> GetMine()
    {
        var callerId = CallerAccountId();
        var requests = Query()
            .Where(r => r.AccountId == callerId)
            .OrderByDescending(r => r.StartDate)
            .ToList();
        return Ok(requests.Select(r => ToDto(r, includeHistory: false)));
    }

    [HttpPost]
    public async Task<ActionResult<TimeOffRequestDto>> Submit(SubmitTimeOffRequest request)
    {
        var account = db.Accounts.Find(CallerAccountId());
        if (account?.LocationId is not { } locationId)
        {
            return BadRequest("Only employees of a location can request time off.");
        }

        var settings = TimeOffRules.SettingsFor(db, locationId);
        if (!settings.TimeOffEnabled)
        {
            return BadRequest("Time-off requests aren't turned on for this location.");
        }

        if (ValidateShape(locationId, request.TimeOffTypeId, request.StartDate, request.EndDate, request.IsPartialDay, request.PartialMinutes) is { } shapeError)
        {
            return BadRequest(shapeError);
        }

        var today = TimeOffRules.LocationToday(settings);
        if (request.StartDate < today)
        {
            return BadRequest("Time off can't start in the past.");
        }

        if (settings.TimeOffMinNoticeDays is { } notice && notice > 0 && request.StartDate < today.AddDays(notice))
        {
            return BadRequest($"Time off must be requested at least {notice} day{(notice == 1 ? "" : "s")} ahead.");
        }

        var blackout = db.TimeOffBlackouts
            .Where(b => b.LocationId == locationId && b.StartDate <= request.EndDate && b.EndDate >= request.StartDate)
            .OrderBy(b => b.StartDate)
            .FirstOrDefault();
        if (blackout is not null)
        {
            return BadRequest(
                $"Time off can't be requested during \"{blackout.Name}\" ({blackout.StartDate:MM/dd/yyyy} – {blackout.EndDate:MM/dd/yyyy}).");
        }

        if (OverlapError(account.Id, request.StartDate, request.EndDate, exceptId: null) is { } overlap)
        {
            return Conflict(overlap);
        }

        var entity = new TimeOffRequest
        {
            AccountId = account.Id,
            LocationId = locationId,
            TimeOffTypeId = request.TimeOffTypeId,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            IsPartialDay = request.IsPartialDay,
            PartialMinutes = request.IsPartialDay ? request.PartialMinutes : null,
            Note = Clean(request.Note),
            Status = TimeOffStatus.Pending,
            CreatedByAccountId = account.Id,
            CreatedAt = DateTime.UtcNow,
        };
        entity.Events.Add(Event(TimeOffEventAction.Submitted, account.Id, null));
        db.TimeOffRequests.Add(entity);
        db.SaveChanges();

        var saved = Load(entity.Id)!;
        await EmailAdmins(saved, EmailTemplateKeys.TimeOffRequested);
        return Ok(ToDto(saved, includeHistory: false));
    }

    // The owner can cancel while Pending, or Approved as long as it hasn't
    // started (no admin approval needed — admins get an email instead). An
    // admin can cancel any Pending or Approved request at their location.
    [HttpPut("{id:int}/cancel")]
    public async Task<ActionResult<TimeOffRequestDto>> Cancel(int id, TimeOffCommentRequest request)
    {
        var entity = Load(id);
        if (entity is null)
        {
            return NotFound();
        }

        var callerId = CallerAccountId();
        var isOwner = entity.AccountId == callerId;
        var isAdmin = IsAdmin() && CanManage(entity.LocationId);
        if (!isOwner && !isAdmin)
        {
            return NotFound();
        }

        if (entity.Status is not (TimeOffStatus.Pending or TimeOffStatus.Approved))
        {
            return BadRequest("Only pending or approved time off can be cancelled.");
        }

        var wasApproved = entity.Status == TimeOffStatus.Approved;
        if (!isAdmin && wasApproved)
        {
            var today = TimeOffRules.LocationToday(TimeOffRules.SettingsFor(db, entity.LocationId));
            if (entity.StartDate <= today)
            {
                return BadRequest("This time off has already started. Ask your manager to change it.");
            }
        }

        entity.Status = TimeOffStatus.Cancelled;
        entity.Events.Add(Event(TimeOffEventAction.Cancelled, callerId, Clean(request.Comment)));
        db.SaveChanges();

        if (wasApproved)
        {
            await NotifySchedule(entity.LocationId);
            if (isOwner && !isAdmin)
            {
                await EmailAdmins(entity, EmailTemplateKeys.TimeOffCancelled);
            }
        }

        return Ok(ToDto(entity, includeHistory: isAdmin));
    }

    [HttpGet]
    [Authorize(Policy = "AdminOrAbove")]
    public ActionResult<IEnumerable<TimeOffRequestDto>> GetForLocation(
        [FromQuery] string? locationCode,
        [FromQuery] TimeOffStatus? status,
        [FromQuery] int? accountId,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to)
    {
        var location = ResolveLocation(locationCode);
        if (location is null)
        {
            return BadRequest("A valid locationCode is required.");
        }

        var query = Query().Where(r => r.LocationId == location.Id);
        if (status is not null) query = query.Where(r => r.Status == status);
        if (accountId is not null) query = query.Where(r => r.AccountId == accountId);
        if (from is not null) query = query.Where(r => r.EndDate >= from);
        if (to is not null) query = query.Where(r => r.StartDate <= to);

        var rows = query.ToList()
            .OrderBy(r => r.Status == TimeOffStatus.Pending ? 0 : 1)
            .ThenBy(r => r.Status == TimeOffStatus.Pending ? r.StartDate.DayNumber : -r.StartDate.DayNumber);
        return Ok(rows.Select(r => ToDto(r, includeHistory: true)));
    }

    [HttpGet("pending-count")]
    [Authorize(Policy = "AdminOrAbove")]
    public ActionResult<int> GetPendingCount([FromQuery] string? locationCode)
    {
        var location = ResolveLocation(locationCode);
        if (location is null)
        {
            return BadRequest("A valid locationCode is required.");
        }

        return Ok(db.TimeOffRequests.Count(r => r.LocationId == location.Id && r.Status == TimeOffStatus.Pending));
    }

    // What approving would do: the shifts already in the range and the
    // minutes each day would count for.
    [HttpGet("{id:int}/preview")]
    [Authorize(Policy = "AdminOrAbove")]
    public ActionResult<TimeOffPreviewDto> Preview(int id)
    {
        var entity = Load(id);
        if (entity is null || !CanManage(entity.LocationId))
        {
            return NotFound();
        }

        return Ok(BuildPreview(entity));
    }

    // Same preview for a request an admin is about to enter themselves.
    [HttpPost("preview")]
    [Authorize(Policy = "AdminOrAbove")]
    public ActionResult<TimeOffPreviewDto> PreviewNew(AdminCreateTimeOffRequest request)
    {
        var account = db.Accounts.Find(request.AccountId);
        if (account?.LocationId is not { } locationId || !CanManage(locationId))
        {
            return BadRequest("Employee must belong to a location you manage.");
        }

        if (ValidateShape(locationId, request.TimeOffTypeId, request.StartDate, request.EndDate, request.IsPartialDay, request.PartialMinutes) is { } error)
        {
            return BadRequest(error);
        }

        return Ok(BuildPreview(new TimeOffRequest
        {
            AccountId = account.Id,
            LocationId = locationId,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            IsPartialDay = request.IsPartialDay,
            PartialMinutes = request.PartialMinutes,
        }));
    }

    [HttpPut("{id:int}/approve")]
    [Authorize(Policy = "AdminOrAbove")]
    public async Task<ActionResult<TimeOffRequestDto>> Approve(int id, ApproveTimeOffRequest request)
    {
        var entity = Load(id);
        if (entity is null || !CanManage(entity.LocationId))
        {
            return NotFound();
        }

        if (entity.Status != TimeOffStatus.Pending)
        {
            return BadRequest("Only pending requests can be approved.");
        }

        if (OverlapError(entity.AccountId, entity.StartDate, entity.EndDate, exceptId: entity.Id, approvedOnly: true) is { } overlap)
        {
            return Conflict(overlap);
        }

        var settings = TimeOffRules.SettingsFor(db, entity.LocationId);
        var days = TimeOffRules.SuggestDays(db, entity, settings);
        if (TimeOffRules.ApplyDayOverrides(days, request.Days) is { } dayError)
        {
            return BadRequest(dayError);
        }

        var callerId = CallerAccountId();
        entity.Status = TimeOffStatus.Approved;
        entity.IsPaid = entity.TimeOffType!.IsPaid;
        entity.ReviewedByAccountId = callerId;
        entity.ReviewedAt = DateTime.UtcNow;
        entity.ReviewComment = Clean(request.Comment);
        entity.Days = days;
        entity.Events.Add(Event(TimeOffEventAction.Approved, callerId, entity.ReviewComment));
        RemoveConflicts(entity, request.RemoveConflictingAssignments);
        db.SaveChanges();

        await NotifySchedule(entity.LocationId);
        var saved = Load(entity.Id)!;
        await EmailEmployee(saved, EmailTemplateKeys.TimeOffApproved);
        return Ok(ToDto(saved, includeHistory: true));
    }

    [HttpPut("{id:int}/deny")]
    [Authorize(Policy = "AdminOrAbove")]
    public async Task<ActionResult<TimeOffRequestDto>> Deny(int id, TimeOffCommentRequest request)
    {
        var entity = Load(id);
        if (entity is null || !CanManage(entity.LocationId))
        {
            return NotFound();
        }

        if (entity.Status != TimeOffStatus.Pending)
        {
            return BadRequest("Only pending requests can be denied.");
        }

        var comment = Clean(request.Comment);
        if (comment is null)
        {
            return BadRequest("Please say why the request is denied.");
        }

        var callerId = CallerAccountId();
        entity.Status = TimeOffStatus.Denied;
        entity.ReviewedByAccountId = callerId;
        entity.ReviewedAt = DateTime.UtcNow;
        entity.ReviewComment = comment;
        entity.Events.Add(Event(TimeOffEventAction.Denied, callerId, comment));
        db.SaveChanges();

        await EmailEmployee(entity, EmailTemplateKeys.TimeOffDenied);
        return Ok(ToDto(entity, includeHistory: true));
    }

    // An admin entering time off for an employee (e.g. they phoned in). It's
    // approved straight away, may start in the past and may fall on a
    // blackout date — those limits are only for employees asking.
    [HttpPost("admin")]
    [Authorize(Policy = "AdminOrAbove")]
    public async Task<ActionResult<TimeOffRequestDto>> AdminCreate(AdminCreateTimeOffRequest request)
    {
        var account = db.Accounts.Find(request.AccountId);
        if (account?.LocationId is not { } locationId || !CanManage(locationId))
        {
            return BadRequest("Employee must belong to a location you manage.");
        }

        if (ValidateShape(locationId, request.TimeOffTypeId, request.StartDate, request.EndDate, request.IsPartialDay, request.PartialMinutes) is { } shapeError)
        {
            return BadRequest(shapeError);
        }

        if (OverlapError(account.Id, request.StartDate, request.EndDate, exceptId: null) is { } overlap)
        {
            return Conflict(overlap);
        }

        var type = db.TimeOffTypes.Find(request.TimeOffTypeId)!;
        var callerId = CallerAccountId();
        var now = DateTime.UtcNow;
        var entity = new TimeOffRequest
        {
            AccountId = account.Id,
            LocationId = locationId,
            TimeOffTypeId = type.Id,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            IsPartialDay = request.IsPartialDay,
            PartialMinutes = request.IsPartialDay ? request.PartialMinutes : null,
            Note = Clean(request.Note),
            Status = TimeOffStatus.Approved,
            IsPaid = type.IsPaid,
            ReviewedByAccountId = callerId,
            ReviewedAt = now,
            CreatedByAccountId = callerId,
            CreatedAt = now,
        };

        var days = TimeOffRules.SuggestDays(db, entity, TimeOffRules.SettingsFor(db, locationId));
        if (TimeOffRules.ApplyDayOverrides(days, request.Days) is { } dayError)
        {
            return BadRequest(dayError);
        }

        entity.Days = days;
        entity.Events.Add(Event(TimeOffEventAction.Submitted, callerId, "Entered by admin"));
        entity.Events.Add(Event(TimeOffEventAction.Approved, callerId, null));
        db.TimeOffRequests.Add(entity);
        RemoveConflicts(entity, request.RemoveConflictingAssignments);
        db.SaveChanges();

        await NotifySchedule(locationId);
        return Ok(ToDto(Load(entity.Id)!, includeHistory: true));
    }

    // Approved time off in a date range, for schedule views. Leads see it
    // too so they don't plan around someone who's away; an employee only
    // ever gets their own.
    [HttpGet("calendar")]
    public ActionResult<IEnumerable<TimeOffCalendarEntryDto>> GetCalendar(
        [FromQuery] string? locationCode, [FromQuery] DateOnly from, [FromQuery] DateOnly to)
    {
        var location = ResolveLocation(locationCode);
        if (location is null)
        {
            return BadRequest("A valid locationCode is required.");
        }

        var query = db.TimeOffRequests
            .Include(r => r.Account)
            .Include(r => r.TimeOffType)
            .Include(r => r.Days)
            .Where(r => r.LocationId == location.Id && r.Status == TimeOffStatus.Approved
                && r.StartDate <= to && r.EndDate >= from);
        var seesEveryone = User.IsInRole(nameof(AccountRole.Sa)) || User.IsInRole(nameof(AccountRole.Admin))
            || User.IsInRole(nameof(AccountRole.Lead));
        if (!seesEveryone)
        {
            var callerId = CallerAccountId();
            query = query.Where(r => r.AccountId == callerId);
        }

        var entries = query.ToList()
            .SelectMany(r => r.Days
                .Where(d => d.Date >= from && d.Date <= to)
                .Select(d => new TimeOffCalendarEntryDto(
                    r.Id, r.AccountId, FullName(r.Account!), d.Date, r.TimeOffType!.Name, r.TimeOffType.Color,
                    r.IsPartialDay, d.Minutes)))
            .OrderBy(e => e.Date).ThenBy(e => e.EmployeeName)
            .ToList();
        return Ok(entries);
    }

    private TimeOffPreviewDto BuildPreview(TimeOffRequest entity)
    {
        var conflicts = db.ShiftAssignments
            .Include(a => a.Shift)
            .Where(a => a.AccountId == entity.AccountId && a.Date >= entity.StartDate && a.Date <= entity.EndDate)
            .OrderBy(a => a.Date)
            .ToList()
            .Select(a => new TimeOffConflictDto(a.Id, a.Date, a.Shift!.Name, a.Shift.StartTime, a.Shift.EndTime, a.IsPublished))
            .ToList();
        var days = TimeOffRules.SuggestDays(db, entity, TimeOffRules.SettingsFor(db, entity.LocationId))
            .Select(d => new TimeOffRequestDayDto(d.Date, d.Minutes))
            .ToList();
        return new TimeOffPreviewDto(conflicts, days);
    }

    // Draft (not yet posted) shifts in a full-day range are always removed —
    // nobody has seen them yet. Posted shifts are removed only when the admin
    // asks. A shift that has already been clocked into, or is part of a
    // call-out cover, is left for the admin to sort out by hand.
    private void RemoveConflicts(TimeOffRequest entity, bool removePublished)
    {
        if (entity.IsPartialDay)
        {
            return;
        }

        var assignments = db.ShiftAssignments
            .Where(a => a.AccountId == entity.AccountId && a.Date >= entity.StartDate && a.Date <= entity.EndDate)
            .ToList();
        var clockedIds = db.TimeEntries
            .Where(t => assignments.Select(a => a.Id).Contains(t.ShiftAssignmentId))
            .Select(t => t.ShiftAssignmentId)
            .ToHashSet();
        var coveredIds = db.ShiftAssignments
            .Where(a => a.CoversAssignmentId != null)
            .Select(a => a.CoversAssignmentId!.Value)
            .ToHashSet();

        foreach (var assignment in assignments)
        {
            var linked = clockedIds.Contains(assignment.Id) || assignment.CoversAssignmentId is not null || coveredIds.Contains(assignment.Id);
            if (linked || (assignment.IsPublished && !removePublished))
            {
                continue;
            }

            db.ShiftAssignments.Remove(assignment);
        }
    }

    private string? ValidateShape(int locationId, int typeId, DateOnly start, DateOnly end, bool isPartial, int? partialMinutes)
    {
        var type = db.TimeOffTypes.Find(typeId);
        if (type is null || type.LocationId != locationId || !type.IsActive)
        {
            return "Pick a time-off type.";
        }

        if (end < start)
        {
            return "The end date can't be before the start date.";
        }

        if (end.DayNumber - start.DayNumber + 1 > MaxRangeDays)
        {
            return $"A single request can cover at most {MaxRangeDays} days.";
        }

        if (isPartial)
        {
            if (start != end)
            {
                return "A partial day has to be a single date.";
            }

            if (partialMinutes is not { } minutes || minutes <= 0 || minutes >= 24 * 60)
            {
                return "Enter how many hours you need off.";
            }
        }

        return null;
    }

    private string? OverlapError(int accountId, DateOnly start, DateOnly end, int? exceptId, bool approvedOnly = false)
    {
        var overlapping = db.TimeOffRequests
            .Where(r => r.AccountId == accountId && r.Id != exceptId
                && (r.Status == TimeOffStatus.Approved || (!approvedOnly && r.Status == TimeOffStatus.Pending))
                && r.StartDate <= end && r.EndDate >= start)
            .FirstOrDefault();
        return overlapping is null
            ? null
            : $"There's already {(overlapping.Status == TimeOffStatus.Approved ? "approved" : "a pending request for")} time off "
              + $"from {overlapping.StartDate:MM/dd/yyyy} to {overlapping.EndDate:MM/dd/yyyy}.";
    }

    private async Task EmailAdmins(TimeOffRequest entity, string key)
    {
        var admins = db.Accounts
            .Where(a => a.LocationId == entity.LocationId && a.Role == AccountRole.Admin && a.IsActive && a.Email != "")
            .Select(a => a.Email)
            .ToList();
        foreach (var email in admins)
        {
            await TrySend(entity, key, email);
        }
    }

    private async Task EmailEmployee(TimeOffRequest entity, string key)
    {
        if (!string.IsNullOrWhiteSpace(entity.Account?.Email))
        {
            await TrySend(entity, key, entity.Account.Email);
        }
    }

    // Best-effort like the other notification emails: no SMTP set up or a
    // send failure is skipped quietly — the request itself is already saved.
    private async Task TrySend(TimeOffRequest entity, string key, string toAddress)
    {
        var settings = db.LocationSettings.SingleOrDefault(s => s.LocationId == entity.LocationId);
        var location = db.Locations.Find(entity.LocationId);
        if (settings is null || location is null || string.IsNullOrWhiteSpace(settings.SmtpHost))
        {
            return;
        }

        var template = db.EmailTemplates.SingleOrDefault(t => t.LocationId == entity.LocationId && t.Key == key)
            ?? EmailTemplateCatalog.Default(key);
        var placeholders = Placeholders(entity, location, settings, $"{Request.Scheme}://{Request.Host}");
        try
        {
            await emailSender.SendAsync(
                settings,
                toAddress,
                EmailTemplateCatalog.Render(template.Subject, placeholders),
                EmailTemplateCatalog.Render(template.BodyHtml, placeholders));
        }
        catch
        {
            // Skipped — see above.
        }
    }

    internal static Dictionary<string, string> Placeholders(
        TimeOffRequest entity, Location location, LocationSettings settings, string baseUrl)
    {
        var dates = entity.StartDate == entity.EndDate
            ? PayrollHoursEmail.FormatDate(entity.StartDate, settings.DateFormat)
            : $"{PayrollHoursEmail.FormatDate(entity.StartDate, settings.DateFormat)} – {PayrollHoursEmail.FormatDate(entity.EndDate, settings.DateFormat)}";
        if (entity.IsPartialDay && entity.PartialMinutes is { } minutes)
        {
            dates += $" ({TimeOffRules.FormatMinutes(minutes)})";
        }

        return new Dictionary<string, string>
        {
            ["{{employeeName}}"] = entity.Account is { } a ? FullName(a) : string.Empty,
            ["{{locationName}}"] = location.Name,
            ["{{timeOffType}}"] = entity.TimeOffType?.Name ?? string.Empty,
            ["{{timeOffDates}}"] = dates,
            ["{{note}}"] = System.Net.WebUtility.HtmlEncode(entity.Note ?? "—"),
            ["{{reviewComment}}"] = System.Net.WebUtility.HtmlEncode(entity.ReviewComment ?? "—"),
            ["{{adminLink}}"] = $"{baseUrl}/{location.LocationCode}/admin/time-off",
            ["{{employeeLink}}"] = $"{baseUrl}/{location.LocationCode}/employee/time-off",
        };
    }

    private IQueryable<TimeOffRequest> Query() =>
        db.TimeOffRequests
            .Include(r => r.Account)
            .Include(r => r.TimeOffType)
            .Include(r => r.ReviewedByAccount)
            .Include(r => r.Days)
            .Include(r => r.Events).ThenInclude(e => e.ActorAccount);

    private TimeOffRequest? Load(int id) => Query().SingleOrDefault(r => r.Id == id);

    private static TimeOffRequestEvent Event(TimeOffEventAction action, int actorId, string? comment) =>
        new() { Action = action, ActorAccountId = actorId, At = DateTime.UtcNow, Comment = comment };

    internal static TimeOffRequestDto ToDto(TimeOffRequest r, bool includeHistory)
    {
        // Before approval there are no saved days yet; show the shape asked
        // for (a partial day's own minutes, otherwise unknown = 0).
        var total = r.Days.Count > 0 ? r.Days.Sum(d => d.Minutes) : r.IsPartialDay ? r.PartialMinutes ?? 0 : 0;
        return new TimeOffRequestDto(
            r.Id,
            r.AccountId,
            r.Account is { } a ? FullName(a) : string.Empty,
            r.TimeOffTypeId,
            r.TimeOffType?.Name ?? string.Empty,
            r.TimeOffType?.Color,
            r.StartDate,
            r.EndDate,
            r.IsPartialDay,
            r.PartialMinutes,
            r.Note,
            r.Status,
            r.IsPaid,
            r.ReviewedByAccount is { } reviewer ? FullName(reviewer) : null,
            r.ReviewedAt,
            r.ReviewComment,
            r.CreatedAt,
            total,
            r.Days.OrderBy(d => d.Date).Select(d => new TimeOffRequestDayDto(d.Date, d.Minutes)).ToList(),
            includeHistory
                ? r.Events.OrderBy(e => e.At).ThenBy(e => e.Id)
                    .Select(e => new TimeOffRequestEventDto(e.Action, e.ActorAccount is { } actor ? FullName(actor) : string.Empty, e.At, e.Comment))
                    .ToList()
                : null);
    }

    private static string FullName(Account a) => $"{a.FirstName} {a.LastName}".Trim();

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private async Task NotifySchedule(int locationId)
    {
        var code = db.Locations.Where(l => l.Id == locationId).Select(l => l.LocationCode).SingleOrDefault();
        if (code is not null)
        {
            await notifier.NotifyLocationChanged(code);
        }
    }

    private bool IsAdmin() => User.IsInRole(nameof(AccountRole.Sa)) || User.IsInRole(nameof(AccountRole.Admin));

    private bool CanManage(int locationId)
    {
        if (User.IsInRole(nameof(AccountRole.Sa)))
        {
            return true;
        }

        return User.IsInRole(nameof(AccountRole.Admin))
            && db.Locations.Any(l => l.Id == locationId && l.LocationCode == CallerLocationCode());
    }

    private Location? ResolveLocation(string? locationCode)
    {
        if (User.IsInRole(nameof(AccountRole.Sa)))
        {
            return string.IsNullOrWhiteSpace(locationCode)
                ? null
                : db.Locations.SingleOrDefault(l => l.LocationCode == locationCode);
        }

        var callerLocationCode = CallerLocationCode();
        return db.Locations.SingleOrDefault(l => l.LocationCode == callerLocationCode);
    }

    private string? CallerLocationCode() => User.FindFirst(TokenService.LocationCodeClaimType)?.Value;

    private int CallerAccountId() => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
