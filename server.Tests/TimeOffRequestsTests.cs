using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Server.Controllers;
using Server.Data;
using Server.Dtos;
using Server.Hubs;
using Server.Models;
using Server.Security;
using Server.Services;

namespace Server.Tests;

// Time-off requests (issue #100). Every controller call gets its own
// DbContext, as a real request does. Dates are relative to today because
// "can't start in the past" uses the real clock.
public sealed class TimeOffRequestsTests : IDisposable
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);
    // Comfortably in the future in every time zone.
    private static readonly DateOnly Soon = Today.AddDays(10);

    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly DbContextOptions<AppDbContext> options;
    private readonly List<AppDbContext> contexts = [];
    private readonly AppDbContext db;
    private readonly RecordingEmailSender emails = new();

    private readonly Location location;
    private readonly Location otherLocation;
    private readonly Shift dayShift; // 09:00–17:00 with a 30m lunch = 7h30m
    private readonly Account admin;
    private readonly Account lead;
    private readonly Account sam;
    private readonly Account alex;
    private readonly Account otherAdmin;

    public TimeOffRequestsTests()
    {
        connection.Open();
        options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        db = NewContext();
        db.Database.EnsureCreated();

        location = new Location { Name = "Test", LocationCode = "t1" };
        otherLocation = new Location { Name = "Other", LocationCode = "t2" };
        db.Locations.AddRange(location, otherLocation);
        db.SaveChanges();

        dayShift = new Shift
        {
            Name = "Day",
            StartTime = new TimeOnly(9, 0),
            EndTime = new TimeOnly(17, 0),
            LocationId = location.Id,
            ScheduledBreaks = [new ScheduledBreak { Kind = BreakKind.Lunch, StartTime = new TimeOnly(12, 0), EndTime = new TimeOnly(12, 30) }],
        };
        db.Shifts.Add(dayShift);
        db.SaveChanges();

        admin = Seed("admin", AccountRole.Admin, location, email: "admin@test");
        lead = Seed("lead", AccountRole.Lead, location);
        sam = Seed("sam", AccountRole.Employee, location, email: "sam@test");
        alex = Seed("alex", AccountRole.Employee, location);
        otherAdmin = Seed("other", AccountRole.Admin, otherLocation);

        db.LocationSettings.Add(new LocationSettings { LocationId = location.Id, SmtpHost = "smtp.test", SmtpFromAddress = "tat@test" });
        db.SaveChanges();
    }

    public void Dispose()
    {
        foreach (var context in contexts)
        {
            context.Dispose();
        }

        connection.Dispose();
    }

    private AppDbContext NewContext()
    {
        var context = new AppDbContext(options);
        contexts.Add(context);
        return context;
    }

    private Account Seed(string username, AccountRole role, Location at, string email = "")
    {
        var account = new Account
        {
            Username = username, FirstName = username, LastName = "T", Role = role, LocationId = at.Id, IsActive = true, Email = email,
        };
        db.Accounts.Add(account);
        db.SaveChanges();
        return account;
    }

    private static ControllerContext ContextFor(AppDbContext context, Account caller)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, caller.Id.ToString()),
            new(ClaimTypes.Role, caller.Role.ToString()),
            new(TokenService.LocationCodeClaimType, context.Locations.Find(caller.LocationId)!.LocationCode),
        };
        return new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) },
        };
    }

    private TimeOffRequestsController Requests(Account caller)
    {
        var context = NewContext();
        return new TimeOffRequestsController(context, new SilentNotifier(), emails) { ControllerContext = ContextFor(context, caller) };
    }

    private TimeOffSetupController Setup(Account caller)
    {
        var context = NewContext();
        return new TimeOffSetupController(context) { ControllerContext = ContextFor(context, caller) };
    }

    private ShiftAssignmentsController Shifts(Account caller)
    {
        var context = NewContext();
        return new ShiftAssignmentsController(context, new SilentNotifier(), emails) { ControllerContext = ContextFor(context, caller) };
    }

    private ReportsController Reports(Account caller)
    {
        var context = NewContext();
        return new ReportsController(context, emails) { ControllerContext = ContextFor(context, caller) };
    }

    private sealed class SilentNotifier : IScheduleNotifier
    {
        public Task NotifyLocationChanged(string locationCode) => Task.CompletedTask;
    }

    private static T Ok<T>(ActionResult<T> result) =>
        Assert.IsAssignableFrom<T>(Assert.IsType<OkObjectResult>(result.Result).Value);

    private TimeOffTypeDto Type(string name) =>
        Ok(Setup(admin).GetAllTypes(null)).Single(t => t.Name == name);

    private async Task<TimeOffRequestDto> Submit(Account who, DateOnly start, DateOnly? end = null, string type = "Vacation")
    {
        var typeId = Type(type).Id;
        return Ok(await Requests(who).Submit(new SubmitTimeOffRequest(typeId, start, end ?? start, false, null, "trip")));
    }

    private ShiftAssignment Assign(Account who, DateOnly date, bool published = true)
    {
        var assignment = new ShiftAssignment { ShiftId = dayShift.Id, AccountId = who.Id, Date = date, IsPublished = published };
        db.ShiftAssignments.Add(assignment);
        db.SaveChanges();
        return assignment;
    }

    private void MakeAvailable(Account who, DateOnly date)
    {
        db.Availabilities.Add(new Availability
        {
            AccountId = who.Id, WeekStartDate = date, IsSubmitted = true,
            Days = [new AvailabilityDay { Date = date, IsAvailable = true }],
        });
        db.SaveChanges();
    }

    [Fact]
    public void A_location_gets_the_default_types_all_unpaid()
    {
        var types = Ok(Setup(sam).GetActiveTypes(null)).ToList();

        Assert.Equal(TimeOffRules.DefaultTypeNames, types.Select(t => t.Name));
        Assert.All(types, t => Assert.False(t.IsPaid));
    }

    [Fact]
    public async Task An_employee_submits_and_admins_are_emailed()
    {
        var request = await Submit(sam, Soon, Soon.AddDays(2));

        Assert.Equal(TimeOffStatus.Pending, request.Status);
        Assert.Null(request.History);
        Assert.Contains(emails.Sent, e => e.To == "admin@test" && e.Subject.Contains("requested time off"));
    }

    [Fact]
    public async Task Past_dates_overlaps_and_partial_ranges_are_rejected_for_employees()
    {
        var vacation = Type("Vacation").Id;

        var past = await Requests(sam).Submit(new SubmitTimeOffRequest(vacation, Today.AddDays(-3), Today.AddDays(-3), false, null, null));
        Assert.IsType<BadRequestObjectResult>(past.Result);

        await Submit(sam, Soon, Soon.AddDays(3));
        var overlap = await Requests(sam).Submit(new SubmitTimeOffRequest(vacation, Soon.AddDays(2), Soon.AddDays(5), false, null, null));
        Assert.IsType<ConflictObjectResult>(overlap.Result);

        var partialRange = await Requests(alex).Submit(new SubmitTimeOffRequest(vacation, Soon, Soon.AddDays(1), true, 120, null));
        Assert.IsType<BadRequestObjectResult>(partialRange.Result);
    }

    [Fact]
    public async Task Minimum_notice_is_enforced()
    {
        Ok(Setup(admin).UpdateSettings(null, new TimeOffSettingsDto(true, 30, 480)));

        var result = await Requests(sam).Submit(new SubmitTimeOffRequest(Type("Vacation").Id, Soon, Soon, false, null, null));

        var error = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("30 days", error.Value as string);
    }

    [Fact]
    public async Task Employees_cant_request_on_a_blackout_but_admins_can_enter_time_off()
    {
        Ok(Setup(admin).CreateBlackout(null, new SaveTimeOffBlackoutRequest("Holiday rush", Soon.AddDays(1), Soon.AddDays(3))));
        var vacation = Type("Vacation").Id;

        var blocked = await Requests(sam).Submit(new SubmitTimeOffRequest(vacation, Soon, Soon.AddDays(1), false, null, null));
        var error = Assert.IsType<BadRequestObjectResult>(blocked.Result);
        Assert.Contains("Holiday rush", error.Value as string);

        var byAdmin = Ok(await Requests(admin).AdminCreate(
            new AdminCreateTimeOffRequest(sam.Id, vacation, Soon.AddDays(2), Soon.AddDays(2), false, null, "funeral", false, null)));
        Assert.Equal(TimeOffStatus.Approved, byAdmin.Status);
    }

    [Fact]
    public async Task Leads_and_other_locations_cant_review()
    {
        // Leads are kept out by the AdminOrAbove policy on the route; an
        // admin of another location is kept out by the location check.
        var request = await Submit(sam, Soon);

        var result = await Requests(otherAdmin).Approve(request.Id, new ApproveTimeOffRequest(null, false, null));

        Assert.IsType<NotFoundResult>(result.Result);
        var policies = typeof(TimeOffRequestsController).GetMethod(nameof(TimeOffRequestsController.Approve))!
            .GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), false)
            .Cast<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>()
            .Select(a => a.Policy);
        Assert.Contains("AdminOrAbove", policies);
    }

    [Fact]
    public async Task An_employee_cant_see_or_cancel_someone_elses_request()
    {
        var request = await Submit(sam, Soon);

        Assert.Empty(Ok(Requests(alex).GetMine()));
        var result = await Requests(alex).Cancel(request.Id, new TimeOffCommentRequest(null));
        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task Approving_saves_days_history_and_paid_flag_and_removes_shifts_when_asked()
    {
        Ok(Setup(admin).UpdateType(Type("Vacation").Id, new SaveTimeOffTypeRequest("Vacation", true, true, 0, null, null)));
        var posted = Assign(sam, Soon);
        Assign(sam, Soon.AddDays(1), published: false);
        var request = await Submit(sam, Soon, Soon.AddDays(2));

        var preview = Ok(Requests(admin).Preview(request.Id));
        Assert.Equal(2, preview.Conflicts.Count);

        var approved = Ok(await Requests(admin).Approve(request.Id, new ApproveTimeOffRequest("Enjoy", true,
            [new TimeOffRequestDayDto(Soon.AddDays(2), 0)])));

        Assert.Equal(TimeOffStatus.Approved, approved.Status);
        Assert.True(approved.IsPaid);
        // Shift day = shift length (7h30m), draft day = shift length, last day overridden to 0.
        Assert.Equal([450, 450, 0], approved.Days.Select(d => d.Minutes));
        Assert.Equal([TimeOffEventAction.Submitted, TimeOffEventAction.Approved], approved.History!.Select(h => h.Action));
        Assert.False(NewContext().ShiftAssignments.Any(a => a.AccountId == sam.Id));
        Assert.Contains(emails.Sent, e => e.To == "sam@test" && e.Subject.Contains("approved"));
        _ = posted;
    }

    [Fact]
    public async Task Approving_keeps_posted_shifts_unless_asked()
    {
        Assign(sam, Soon);
        Assign(sam, Soon.AddDays(1), published: false);
        var request = await Submit(sam, Soon, Soon.AddDays(1));

        Ok(await Requests(admin).Approve(request.Id, new ApproveTimeOffRequest(null, false, null)));

        var left = NewContext().ShiftAssignments.Where(a => a.AccountId == sam.Id).ToList();
        Assert.Single(left);
        Assert.True(left[0].IsPublished);
    }

    [Fact]
    public async Task Denying_needs_a_reason()
    {
        var request = await Submit(sam, Soon);

        Assert.IsType<BadRequestObjectResult>((await Requests(admin).Deny(request.Id, new TimeOffCommentRequest(" "))).Result);
        var denied = Ok(await Requests(admin).Deny(request.Id, new TimeOffCommentRequest("Short-staffed")));

        Assert.Equal(TimeOffStatus.Denied, denied.Status);
        Assert.Equal("Short-staffed", denied.ReviewComment);
    }

    [Fact]
    public async Task An_employee_can_cancel_approved_time_off_and_admins_are_told()
    {
        var request = await Submit(sam, Soon);
        Ok(await Requests(admin).Approve(request.Id, new ApproveTimeOffRequest(null, false, null)));
        emails.Sent.Clear();

        var cancelled = Ok(await Requests(sam).Cancel(request.Id, new TimeOffCommentRequest(null)));

        Assert.Equal(TimeOffStatus.Cancelled, cancelled.Status);
        Assert.Contains(emails.Sent, e => e.To == "admin@test" && e.Subject.Contains("cancelled"));
    }

    [Fact]
    public async Task An_employee_cant_cancel_approved_time_off_that_has_started()
    {
        var started = Ok(await Requests(admin).AdminCreate(new AdminCreateTimeOffRequest(
            sam.Id, Type("Vacation").Id, Today.AddDays(-2), Today.AddDays(5), false, null, null, false, null)));

        var result = await Requests(sam).Cancel(started.Id, new TimeOffCommentRequest(null));

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Approved_full_day_off_blocks_scheduling_and_cover()
    {
        MakeAvailable(sam, Soon);
        var request = await Submit(sam, Soon);
        Ok(await Requests(admin).Approve(request.Id, new ApproveTimeOffRequest(null, false, null)));

        var create = await Shifts(admin).Create(new CreateShiftAssignmentRequest(dayShift.Id, sam.Id, Soon));

        var error = Assert.IsType<BadRequestObjectResult>(create.Result);
        Assert.Contains("approved time off", error.Value as string);

        var absent = Assign(alex, Soon);
        var candidates = Ok(Shifts(admin).GetCoverCandidates(absent.Id));
        Assert.DoesNotContain(candidates.Candidates, c => c.AccountId == sam.Id);
    }

    [Fact]
    public async Task Development_mode_skips_the_time_off_block()
    {
        var settings = db.LocationSettings.Single(s => s.LocationId == location.Id);
        settings.DevelopmentMode = true;
        db.SaveChanges();
        var request = await Submit(sam, Soon);
        Ok(await Requests(admin).Approve(request.Id, new ApproveTimeOffRequest(null, false, null)));

        Ok(await Shifts(admin).Create(new CreateShiftAssignmentRequest(dayShift.Id, sam.Id, Soon)));
    }

    [Fact]
    public async Task Payroll_report_splits_paid_and_unpaid_and_ignores_them_for_overtime()
    {
        Ok(Setup(admin).UpdateType(Type("Vacation").Id, new SaveTimeOffTypeRequest("Vacation", true, true, 0, null, null)));
        var start = Today.AddDays(-7);
        Ok(await Requests(admin).AdminCreate(new AdminCreateTimeOffRequest(
            sam.Id, Type("Vacation").Id, start, start.AddDays(1), false, null, null, false, null)));
        Ok(await Requests(admin).AdminCreate(new AdminCreateTimeOffRequest(
            sam.Id, Type("Personal").Id, start.AddDays(2), start.AddDays(2), true, 120, null, false, null)));

        var row = Assert.Single(Ok(Reports(admin).GetHoursReport(null, start, start.AddDays(6))));

        Assert.Equal(960, row.TotalPaidTimeOffMinutes);
        Assert.Equal(120, row.TotalUnpaidTimeOffMinutes);
        Assert.Equal(0, row.TotalOvertimeMinutes);
        Assert.Equal(960, PayrollHoursEmail.TotalPaidMinutes(row));
        Assert.Equal(["Vacation 8h"], row.Days.First(d => d.Date == start).TimeOffBreakdown);
        Assert.Equal(["Personal 2h"], row.Days.First(d => d.Date == start.AddDays(2)).TimeOffBreakdown);
    }

    [Fact]
    public async Task Changing_a_types_paid_setting_doesnt_change_approved_time_off()
    {
        var start = Today.AddDays(-7);
        Ok(await Requests(admin).AdminCreate(new AdminCreateTimeOffRequest(
            sam.Id, Type("Vacation").Id, start, start, false, null, null, false, null)));

        Ok(Setup(admin).UpdateType(Type("Vacation").Id, new SaveTimeOffTypeRequest("Vacation", true, true, 0, null, null)));

        var row = Assert.Single(Ok(Reports(admin).GetHoursReport(null, start, start)));
        Assert.Equal(0, row.TotalPaidTimeOffMinutes);
        Assert.Equal(480, row.TotalUnpaidTimeOffMinutes);
    }

    [Fact]
    public async Task Used_types_cant_be_deleted_and_inactive_ones_cant_be_requested()
    {
        var personal = Type("Personal");
        await Submit(sam, Soon, type: "Personal");

        Assert.IsType<ConflictObjectResult>(Setup(admin).DeleteType(personal.Id));
        Assert.IsType<NoContentResult>(Setup(admin).DeleteType(Type("Jury Duty").Id));

        Ok(Setup(admin).UpdateType(personal.Id, new SaveTimeOffTypeRequest("Personal", false, false, 1, null, null)));
        var result = await Requests(alex).Submit(new SubmitTimeOffRequest(personal.Id, Soon, Soon, false, null, null));
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public void Sick_cant_be_added_as_a_type()
    {
        var result = Setup(admin).CreateType(null, new SaveTimeOffTypeRequest("sick", true, true, 9, null, null));

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Turning_time_off_off_stops_new_requests()
    {
        Ok(Setup(admin).UpdateSettings(null, new TimeOffSettingsDto(false, null, 480)));

        var result = await Requests(sam).Submit(new SubmitTimeOffRequest(Type("Vacation").Id, Soon, Soon, false, null, null));

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task The_calendar_shows_leads_everyone_and_employees_only_themselves()
    {
        var a = await Submit(sam, Soon);
        var b = await Submit(alex, Soon);
        Ok(await Requests(admin).Approve(a.Id, new ApproveTimeOffRequest(null, false, null)));
        Ok(await Requests(admin).Approve(b.Id, new ApproveTimeOffRequest(null, false, null)));

        Assert.Equal(2, Ok(Requests(lead).GetCalendar(null, Soon, Soon)).Count());
        Assert.Equal([sam.Id], Ok(Requests(sam).GetCalendar(null, Soon, Soon)).Select(e => e.AccountId));
    }
}
