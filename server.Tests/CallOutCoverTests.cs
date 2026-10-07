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

// The call-out flow (docs/tickets/call-out-cover.md): marking an employee
// absent and putting someone else on the shift in one step. Every controller
// call gets its own DbContext, as a real request does, so nothing here
// passes just because an earlier call left entities tracked.
public sealed class CallOutCoverTests : IDisposable
{
    // 2026-09-14 is a Monday.
    private static readonly DateOnly Monday = new(2026, 9, 14);

    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly DbContextOptions<AppDbContext> options;
    private readonly List<AppDbContext> contexts = [];
    private readonly AppDbContext db;
    private readonly RecordingEmailSender emails = new();

    private readonly Location location;
    private readonly Location otherLocation;
    private readonly Shift morning; // 07:00–15:00, lunch 11:00–11:30, break 09:00–09:15
    private readonly Shift evening; // 15:00–23:00, lunch 19:00–19:30
    private readonly Shift mid; // 12:00–20:00, overlaps both
    private readonly Account admin;
    private readonly Account lead;
    private readonly Account sam; // the one who calls out
    private readonly Account alex; // the usual cover

    public CallOutCoverTests()
    {
        connection.Open();
        options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        db = NewContext();
        db.Database.EnsureCreated();

        location = new Location { Name = "Test", LocationCode = "t1" };
        otherLocation = new Location { Name = "Other", LocationCode = "t2" };
        db.Locations.AddRange(location, otherLocation);
        db.SaveChanges();

        morning = SeedShift("Morning", 7, 15, lunchAt: 11, breakAt: 9);
        evening = SeedShift("Evening", 15, 23, lunchAt: 19);
        mid = SeedShift("Mid", 12, 20, lunchAt: 16);

        admin = Seed("admin", AccountRole.Admin, location);
        lead = Seed("lead", AccountRole.Lead, location);
        sam = Seed("sam", AccountRole.Employee, location);
        alex = Seed("alex", AccountRole.Employee, location);
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

    private Shift SeedShift(string name, int startHour, int endHour, int lunchAt, int? breakAt = null, Location? at = null)
    {
        var shift = new Shift
        {
            Name = name,
            StartTime = new TimeOnly(startHour, 0),
            EndTime = new TimeOnly(endHour, 0),
            LocationId = (at ?? location).Id,
            ScheduledBreaks =
            [
                new ScheduledBreak { Kind = BreakKind.Lunch, StartTime = new TimeOnly(lunchAt, 0), EndTime = new TimeOnly(lunchAt, 30) },
            ],
        };
        if (breakAt is { } hour)
        {
            shift.ScheduledBreaks.Add(
                new ScheduledBreak { Kind = BreakKind.Break, StartTime = new TimeOnly(hour, 0), EndTime = new TimeOnly(hour, 15) });
        }

        db.Shifts.Add(shift);
        db.SaveChanges();
        return shift;
    }

    private Account Seed(string username, AccountRole role, Location? at, bool active = true, string email = "")
    {
        var account = new Account
        {
            Username = username,
            FirstName = username,
            LastName = "Tester",
            Role = role,
            LocationId = at?.Id,
            IsActive = active,
            Email = email,
        };
        db.Accounts.Add(account);
        db.SaveChanges();
        return account;
    }

    private ShiftAssignment Assign(Shift shift, Account account, DateOnly? date = null, bool published = true)
    {
        var assignment = new ShiftAssignment
        {
            ShiftId = shift.Id,
            AccountId = account.Id,
            Date = date ?? Monday,
            IsPublished = published,
        };
        db.ShiftAssignments.Add(assignment);
        db.SaveChanges();
        return assignment;
    }

    private void MakeAvailable(Account account, DateOnly? date = null)
    {
        db.Availabilities.Add(new Availability
        {
            AccountId = account.Id,
            WeekStartDate = Monday,
            IsSubmitted = true,
            Days = [new AvailabilityDay { Date = date ?? Monday, IsAvailable = true }],
        });
        db.SaveChanges();
    }

    private ShiftAssignmentsController As(Account caller)
    {
        var context = NewContext();
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, caller.Id.ToString()),
            new(ClaimTypes.Role, caller.Role.ToString()),
        };
        if (caller.LocationId is { } locationId)
        {
            claims.Add(new Claim(TokenService.LocationCodeClaimType, context.Locations.Find(locationId)!.LocationCode));
        }

        return new ShiftAssignmentsController(context, new SilentNotifier(), emails)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) },
            },
        };
    }

    private sealed class SilentNotifier : IScheduleNotifier
    {
        public Task NotifyLocationChanged(string locationCode) => Task.CompletedTask;
    }

    private static T Ok<T>(ActionResult<T> result) =>
        Assert.IsType<T>(Assert.IsType<OkObjectResult>(result.Result).Value);

    private static CallOutRequest CallOutWith(Account? cover, int? sickMinutes = null,
        bool confirmUnavailable = true, bool confirmCombine = false, bool sendEmail = false, string? note = "Called in sick") =>
        new(note, sickMinutes, cover?.Id, confirmUnavailable, confirmCombine, sendEmail);

    // A fresh read, the way the next request would see it.
    private ShiftAssignment Reload(int id) =>
        NewContext().ShiftAssignments
            .Include(a => a.Shift).ThenInclude(s => s!.ScheduledBreaks)
            .Single(a => a.Id == id);

    private ShiftAssignment? CoverOf(ShiftAssignment absent) =>
        NewContext().ShiftAssignments.SingleOrDefault(a => a.CoversAssignmentId == absent.Id);

    [Fact]
    public async Task A_call_out_with_cover_keeps_the_absent_shift_and_adds_a_live_linked_cover_shift()
    {
        var absent = Assign(morning, sam);

        var result = Ok(await As(admin).CallOut(absent.Id, CallOutWith(alex, sickMinutes: 240)));

        Assert.Equal(sam.Id, result.Absent.AccountId);
        Assert.True(result.Absent.IsAbsent);
        Assert.Equal("Called in sick", result.Absent.AbsenceNote);
        Assert.Equal(240, result.Absent.SickMinutes);
        Assert.Equal("alex", result.Absent.CoveredByAccountFirstName);

        var cover = Assert.IsType<ShiftAssignmentDto>(result.Cover);
        Assert.Equal(alex.Id, cover.AccountId);
        Assert.Equal(morning.Id, cover.ShiftId);
        Assert.Equal(Monday, cover.Date);
        Assert.True(cover.IsPublished);
        Assert.Equal(absent.Id, cover.CoversAssignmentId);
        Assert.Equal("sam", cover.CoversAccountFirstName);
        Assert.Equal(result.Absent.CoveredByAssignmentId, cover.Id);

        var saved = Reload(cover.Id);
        Assert.Equal(admin.Id, saved.CoverAssignedByAccountId);
        Assert.NotNull(saved.CoverAssignedAt);
        Assert.NotNull(saved.PublishedAt);
    }

    [Fact]
    public async Task The_schedule_names_both_sides_of_a_cover_pairing()
    {
        var absent = Assign(morning, sam);
        Ok(await As(admin).CallOut(absent.Id, CallOutWith(alex)));

        var week = Assert.IsAssignableFrom<IEnumerable<ShiftAssignmentDto>>(
            Assert.IsType<OkObjectResult>(As(admin).GetForWeek(null, Monday).Result).Value).ToList();

        Assert.Equal("alex", week.Single(a => a.AccountId == sam.Id).CoveredByAccountFirstName);
        Assert.Equal("sam", week.Single(a => a.AccountId == alex.Id).CoversAccountFirstName);
    }

    [Fact]
    public async Task A_call_out_without_cover_just_marks_absent_and_wipes_a_stray_punch()
    {
        var absent = Assign(morning, sam);
        db.TimeEntries.Add(new TimeEntry { AccountId = sam.Id, ShiftAssignmentId = absent.Id, ClockInAt = DateTime.UtcNow });
        db.SaveChanges();

        var result = Ok(await As(lead).CallOut(absent.Id, CallOutWith(null)));

        Assert.True(result.Absent.IsAbsent);
        Assert.Null(result.Cover);
        Assert.Null(result.Absent.CoveredByAssignmentId);
        Assert.Empty(NewContext().TimeEntries);
    }

    [Fact]
    public async Task A_call_out_needs_a_note()
    {
        var absent = Assign(morning, sam);

        var result = await As(admin).CallOut(absent.Id, CallOutWith(alex, note: " "));

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.False(Reload(absent.Id).IsAbsent);
        Assert.Null(CoverOf(absent));
    }

    [Fact]
    public async Task Cover_on_a_draft_shift_is_a_draft_too()
    {
        var absent = Assign(morning, sam, published: false);

        var result = Ok(await As(admin).CallOut(absent.Id, CallOutWith(alex)));

        Assert.False(result.Cover!.IsPublished);
        Assert.Null(Reload(result.Cover.Id).PublishedAt);
    }

    [Fact]
    public async Task A_cover_employee_who_cannot_be_scheduled_is_rejected_and_nothing_is_saved()
    {
        var absent = Assign(morning, sam);
        var elsewhere = Seed("elsewhere", AccountRole.Employee, otherLocation);
        var inactive = Seed("inactive", AccountRole.Employee, location, active: false);
        var sa = Seed("sa", AccountRole.Sa, location);

        foreach (var cover in new[] { elsewhere, inactive, sa, sam })
        {
            var result = await As(admin).CallOut(absent.Id, CallOutWith(cover, sickMinutes: 60));

            Assert.IsType<BadRequestObjectResult>(result.Result);
        }

        var saved = Reload(absent.Id);
        Assert.False(saved.IsAbsent);
        Assert.Equal(0, saved.SickMinutes);
        Assert.Null(CoverOf(absent));
    }

    [Fact]
    public async Task An_unavailable_employee_can_cover_only_once_the_warning_is_confirmed()
    {
        var absent = Assign(morning, sam);

        var refused = await As(admin).CallOut(absent.Id, CallOutWith(alex, confirmUnavailable: false));

        Assert.IsType<ConflictObjectResult>(refused.Result);
        Assert.False(Reload(absent.Id).IsAbsent);

        var confirmed = Ok(await As(admin).CallOut(absent.Id, CallOutWith(alex, confirmUnavailable: true)));
        Assert.Equal(alex.Id, confirmed.Cover!.AccountId);
    }

    [Fact]
    public async Task An_available_employee_needs_no_confirmation_and_development_mode_waives_availability()
    {
        var absent = Assign(morning, sam);
        MakeAvailable(alex);
        Ok(await As(admin).CallOut(absent.Id, CallOutWith(alex, confirmUnavailable: false)));

        var tuesday = Assign(morning, sam, Monday.AddDays(1));
        db.LocationSettings.Add(new LocationSettings { LocationId = location.Id, DevelopmentMode = true });
        db.SaveChanges();
        Ok(await As(admin).CallOut(tuesday.Id, CallOutWith(alex, confirmUnavailable: false)));
    }

    [Fact]
    public async Task Create_still_blocks_unavailable_employees_and_second_shifts()
    {
        var unavailable = await As(admin).Create(new CreateShiftAssignmentRequest(morning.Id, alex.Id, Monday));
        Assert.IsType<BadRequestObjectResult>(unavailable.Result);

        MakeAvailable(alex);
        Assign(morning, alex);
        var second = await As(admin).Create(new CreateShiftAssignmentRequest(evening.Id, alex.Id, Monday));
        Assert.IsType<ConflictObjectResult>(second.Result);
    }

    [Fact]
    public async Task Someone_already_working_that_day_gets_one_combined_shift_once_confirmed()
    {
        var own = Assign(morning, alex);
        var absent = Assign(evening, sam);

        var refused = await As(admin).CallOut(absent.Id, CallOutWith(alex, confirmCombine: false));
        Assert.IsType<ConflictObjectResult>(refused.Result);
        Assert.False(Reload(absent.Id).IsAbsent);
        Assert.Equal(morning.Id, Reload(own.Id).ShiftId);

        var cover = Ok(await As(admin).CallOut(absent.Id, CallOutWith(alex, confirmCombine: true))).Cover!;

        // Still one assignment for alex that day — their own, now on a
        // 07:00–23:00 shift that isn't one of the location's templates.
        Assert.Equal(own.Id, cover.Id);
        Assert.Equal(1, NewContext().ShiftAssignments.Count(a => a.AccountId == alex.Id && a.Date == Monday));
        Assert.Equal("Morning + Evening", cover.ShiftName);
        Assert.Equal(new TimeOnly(7, 0), cover.ShiftStartTime);
        Assert.Equal(new TimeOnly(23, 0), cover.ShiftEndTime);
        Assert.Equal("Morning", cover.OriginalShiftName);
        Assert.Equal(absent.Id, cover.CoversAssignmentId);
        Assert.Equal("sam", cover.CoversAccountFirstName);

        // One lunch (their own), plus the short break: 16h less 30 minutes.
        Assert.Equal(15.5, cover.Hours);
        var lunch = Assert.Single(cover.ScheduledBreaks, b => b.Kind == BreakKind.Lunch);
        Assert.Equal(new TimeOnly(11, 0), lunch.StartTime);
        Assert.Single(cover.ScheduledBreaks, b => b.Kind == BreakKind.Break);

        var saved = Reload(own.Id);
        Assert.Equal(morning.Id, saved.OriginalShiftId);
        Assert.False(saved.Shift!.IsActive);
        Assert.Equal(location.Id, saved.Shift.LocationId);
    }

    [Fact]
    public async Task A_location_shift_can_be_picked_as_the_replacement_instead_of_a_combined_one()
    {
        var allDay = SeedShift("All Day", 7, 23, lunchAt: 13);
        var own = Assign(morning, alex);
        var absent = Assign(evening, sam);

        var cover = Ok(await As(lead).CallOut(
            absent.Id, CallOutWith(alex, confirmCombine: true) with { CoverShiftId = allDay.Id })).Cover!;

        Assert.Equal(own.Id, cover.Id);
        Assert.Equal(allDay.Id, cover.ShiftId);
        Assert.Equal("All Day", cover.ShiftName);
        Assert.Equal("Morning", cover.OriginalShiftName);
        Assert.Equal(absent.Id, cover.CoversAssignmentId);
        Assert.Equal(new TimeOnly(13, 0), Assert.Single(cover.ScheduledBreaks).StartTime);
        // No throwaway combined shift was created.
        Assert.Empty(NewContext().Shifts.Where(s => !s.IsActive));

        // Removing the cover still puts them back on their own shift.
        Ok(await As(lead).RemoveCover(absent.Id));
        Assert.Equal(morning.Id, Reload(own.Id).ShiftId);
    }

    [Fact]
    public async Task The_replacement_shift_can_be_changed_for_someone_already_covering()
    {
        var allDay = SeedShift("All Day", 7, 23, lunchAt: 13);
        var own = Assign(morning, alex);
        var absent = Assign(evening, sam);
        Ok(await As(admin).CallOut(absent.Id, CallOutWith(alex, confirmCombine: true)));

        var result = Ok(await As(admin).AssignCover(absent.Id, new AssignCoverRequest(alex.Id, CoverShiftId: allDay.Id)));

        Assert.Equal(own.Id, result.Cover!.Id);
        Assert.Equal("All Day", result.Cover.ShiftName);
        var saved = Reload(own.Id);
        Assert.Equal(allDay.Id, saved.ShiftId);
        Assert.Equal(morning.Id, saved.OriginalShiftId);
        Assert.Equal(allDay.Id, Ok(As(admin).GetCoverCandidates(absent.Id)).CurrentCoverShiftId);
    }

    [Fact]
    public async Task A_replacement_shift_must_be_an_active_shift_at_the_location()
    {
        var inactive = SeedShift("Old", 7, 23, lunchAt: 13);
        inactive.IsActive = false;
        db.SaveChanges();
        var elsewhere = SeedShift("Elsewhere", 7, 23, lunchAt: 13, at: otherLocation);
        var own = Assign(morning, alex);
        var absent = Assign(evening, sam);

        foreach (var shift in new[] { inactive, elsewhere })
        {
            var result = await As(admin).CallOut(
                absent.Id, CallOutWith(alex, confirmCombine: true) with { CoverShiftId = shift.Id });

            Assert.IsType<BadRequestObjectResult>(result.Result);
            Assert.IsType<BadRequestObjectResult>(As(admin).GetCoverCandidates(absent.Id, shift.Id).Result);
        }

        Assert.False(Reload(absent.Id).IsAbsent);
        Assert.Equal(morning.Id, Reload(own.Id).ShiftId);
    }

    [Fact]
    public void Candidates_offer_the_locations_shifts_and_preview_a_picked_one()
    {
        var allDay = SeedShift("All Day", 7, 23, lunchAt: 13);
        var longDay = SeedShift("Long Day", 7, 21, lunchAt: 13);
        Assign(morning, alex);
        var absent = Assign(evening, sam);

        var automatic = Ok(As(admin).GetCoverCandidates(absent.Id));
        Assert.Equal(
            ["Morning", "Long Day", "All Day", "Mid", "Evening"],
            automatic.Shifts.Select(s => s.Name).ToList());
        Assert.Null(automatic.CurrentCoverShiftId);
        var byDefault = automatic.Candidates.Single(c => c.AccountId == alex.Id);
        Assert.Equal("Morning + Evening", byDefault.CombinedShift!.ShiftName);
        // A location shift with exactly the combined span is suggested.
        Assert.Equal(allDay.Id, byDefault.SuggestedShiftId);
        Assert.Null(automatic.Candidates.Single(c => c.AccountId == lead.Id).SuggestedShiftId);

        // 7:00–21:00 less a 30-minute lunch: 13.5h, 5.5h past the daily 8.
        var picked = Ok(As(admin).GetCoverCandidates(absent.Id, longDay.Id)).Candidates.Single(c => c.AccountId == alex.Id);
        Assert.Equal("Long Day", picked.CombinedShift!.ShiftName);
        Assert.Equal(13.5, picked.CombinedShift.Hours);
        Assert.Equal(13.5, picked.WeekScheduledHours);
        Assert.Equal(330, picked.OvertimeMinutes);
        Assert.Equal("Morning", picked.OwnShift!.ShiftName);
    }

    [Fact]
    public async Task Overlapping_shifts_combine_into_their_whole_span()
    {
        var own = Assign(mid, alex); // 12:00–20:00
        var absent = Assign(evening, sam); // 15:00–23:00

        var cover = Ok(await As(admin).CallOut(absent.Id, CallOutWith(alex, confirmCombine: true))).Cover!;

        Assert.Equal(own.Id, cover.Id);
        Assert.Equal("Mid + Evening", cover.ShiftName);
        Assert.Equal(new TimeOnly(12, 0), cover.ShiftStartTime);
        Assert.Equal(new TimeOnly(23, 0), cover.ShiftEndTime);
        Assert.Equal(10.5, cover.Hours);
    }

    [Fact]
    public async Task Punches_already_made_on_the_own_shift_carry_over_to_the_combined_one()
    {
        var own = Assign(morning, alex);
        var absent = Assign(evening, sam);
        db.TimeEntries.Add(new TimeEntry { AccountId = alex.Id, ShiftAssignmentId = own.Id, ClockInAt = DateTime.UtcNow });
        db.SaveChanges();

        var cover = Ok(await As(admin).CallOut(absent.Id, CallOutWith(alex, confirmCombine: true))).Cover!;

        var entry = Assert.Single(NewContext().TimeEntries);
        Assert.Equal(cover.Id, entry.ShiftAssignmentId);
        Assert.Null(entry.ClockOutAt);
    }

    [Fact]
    public async Task Someone_who_has_clocked_out_or_is_already_covering_cannot_be_combined()
    {
        var own = Assign(morning, alex);
        var absent = Assign(evening, sam);
        var entry = new TimeEntry
        {
            AccountId = alex.Id, ShiftAssignmentId = own.Id, ClockInAt = DateTime.UtcNow.AddHours(-8), ClockOutAt = DateTime.UtcNow,
        };
        db.TimeEntries.Add(entry);
        db.SaveChanges();

        var clockedOut = await As(admin).CallOut(absent.Id, CallOutWith(alex, confirmCombine: true));
        Assert.IsType<ConflictObjectResult>(clockedOut.Result);

        // Back on the clock, alex covers sam — and then can't also cover jo.
        NewContext().TimeEntries.Where(t => t.Id == entry.Id).ExecuteDelete();
        Ok(await As(admin).CallOut(absent.Id, CallOutWith(alex, confirmCombine: true)));
        var jo = Seed("jo", AccountRole.Employee, location);
        var second = Assign(mid, jo);

        var alreadyCovering = await As(admin).CallOut(second.Id, CallOutWith(alex, confirmCombine: true));
        Assert.IsType<ConflictObjectResult>(alreadyCovering.Result);
        Assert.False(Reload(second.Id).IsAbsent);
    }

    [Fact]
    public async Task The_same_two_shifts_reuse_one_combined_shift()
    {
        var first = Ok(await As(admin).CallOut(
            AssignPair(Monday).Id, CallOutWith(alex, confirmCombine: true))).Cover!;
        var second = Ok(await As(admin).CallOut(
            AssignPair(Monday.AddDays(1)).Id, CallOutWith(alex, confirmCombine: true))).Cover!;

        Assert.Equal(first.ShiftId, second.ShiftId);
        Assert.Equal(1, NewContext().Shifts.Count(s => !s.IsActive));

        // alex on Morning, sam absent on Evening.
        ShiftAssignment AssignPair(DateOnly date)
        {
            Assign(morning, alex, date);
            return Assign(evening, sam, date);
        }
    }

    [Fact]
    public async Task Removing_cover_puts_a_combined_employee_back_on_their_own_shift_even_after_clocking_in()
    {
        var own = Assign(morning, alex);
        var absent = Assign(evening, sam);
        Ok(await As(admin).CallOut(absent.Id, CallOutWith(alex, confirmCombine: true)));
        db.TimeEntries.Add(new TimeEntry { AccountId = alex.Id, ShiftAssignmentId = own.Id, ClockInAt = DateTime.UtcNow });
        db.SaveChanges();

        var result = Ok(await As(lead).RemoveCover(absent.Id));

        Assert.True(result.IsAbsent);
        Assert.Null(result.CoveredByAssignmentId);
        var restored = Reload(own.Id);
        Assert.Equal(morning.Id, restored.ShiftId);
        Assert.Null(restored.OriginalShiftId);
        Assert.Null(restored.CoversAssignmentId);
        Assert.Null(restored.CoverAssignedAt);
        Assert.Single(NewContext().TimeEntries);
    }

    [Fact]
    public async Task Changing_cover_from_a_combined_employee_restores_their_shift_and_combines_the_new_one()
    {
        var alexOwn = Assign(morning, alex);
        var jo = Seed("jo", AccountRole.Employee, location);
        var joOwn = Assign(mid, jo);
        var absent = Assign(evening, sam);
        Ok(await As(admin).CallOut(absent.Id, CallOutWith(alex, confirmCombine: true)));

        var result = Ok(await As(admin).AssignCover(
            absent.Id, new AssignCoverRequest(jo.Id, ConfirmUnavailable: true, ConfirmCombine: true)));

        Assert.Equal(joOwn.Id, result.Cover!.Id);
        Assert.Equal("Mid + Evening", result.Cover.ShiftName);
        Assert.Equal("jo", result.Absent.CoveredByAccountFirstName);
        var alexAgain = Reload(alexOwn.Id);
        Assert.Equal(morning.Id, alexAgain.ShiftId);
        Assert.Null(alexAgain.CoversAssignmentId);
    }

    [Fact]
    public async Task Someone_who_called_out_that_day_cannot_cover()
    {
        var alexShift = Assign(morning, alex);
        Ok(await As(admin).CallOut(alexShift.Id, CallOutWith(null)));
        var absent = Assign(evening, sam);

        var result = await As(admin).CallOut(absent.Id, CallOutWith(alex, confirmCombine: true));

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task A_cover_shift_is_live_without_reposting_the_week()
    {
        // An already-started shift today, so the clock-in window is open
        // whenever this runs.
        var today = DateOnly.FromDateTime(DateTime.Now);
        var started = SeedShift("Started", 0, 0, lunchAt: 0);
        started.EndTime = new TimeOnly(23, 59);
        db.SaveChanges();
        var absent = Assign(started, sam, today);

        var cover = Ok(await As(admin).CallOut(absent.Id, CallOutWith(alex))).Cover!;

        // No Publish call in between.
        var punch = new TimeEntryPunchService(NewContext()).ClockIn(alex.Id, cover.Id, clientIp: null, bypassDeviceCheck: true);
        Assert.Equal(StatusCodes.Status200OK, punch.StatusCode);
    }

    [Fact]
    public async Task A_draft_own_shift_goes_live_when_it_is_combined_with_a_posted_one()
    {
        var own = Assign(morning, alex, published: false);
        var absent = Assign(evening, sam);

        var cover = Ok(await As(admin).CallOut(absent.Id, CallOutWith(alex, confirmCombine: true))).Cover!;

        Assert.True(cover.IsPublished);
        Assert.NotNull(Reload(own.Id).PublishedAt);
    }

    [Theory]
    [InlineData(7, 15, 15, 23, 7, 23, 0)] // back to back
    [InlineData(15, 23, 7, 15, 7, 23, 0)] // cover shift comes first
    [InlineData(9, 17, 14, 22, 9, 22, 0)] // overlapping
    [InlineData(7, 11, 15, 23, 7, 23, 240)] // a gap is worked through
    [InlineData(18, 22, 22, 6, 18, 6, 0)] // into an overnight shift
    public void A_combined_shift_runs_from_the_earlier_start_to_the_later_end(
        int ownStart, int ownEnd, int coverStart, int coverEnd, int expectedStart, int expectedEnd, int expectedGap)
    {
        var own = new Shift { Name = "Own", StartTime = new TimeOnly(ownStart, 0), EndTime = new TimeOnly(ownEnd, 0) };
        var cover = new Shift { Name = "Cover", StartTime = new TimeOnly(coverStart, 0), EndTime = new TimeOnly(coverEnd, 0) };

        var combined = ShiftAssignmentsController.BuildCombinedShift(own, cover);

        Assert.Equal(new TimeOnly(expectedStart, 0), combined.StartTime);
        Assert.Equal(new TimeOnly(expectedEnd, 0), combined.EndTime);
        Assert.Equal(expectedGap, ShiftAssignmentsController.GapMinutes(own, cover));
        Assert.Equal(ownStart <= coverStart ? "Own + Cover" : "Cover + Own", combined.Name);
        Assert.False(combined.IsActive);
    }

    [Fact]
    public void A_combined_shift_has_one_lunch_and_every_short_break()
    {
        static ScheduledBreak Window(BreakKind kind, int hour, int minutes) =>
            new() { Kind = kind, StartTime = new TimeOnly(hour, 0), EndTime = new TimeOnly(hour, minutes) };
        var withLunch = new Shift
        {
            Name = "A", StartTime = new TimeOnly(7, 0), EndTime = new TimeOnly(15, 0),
            ScheduledBreaks = [Window(BreakKind.Lunch, 11, 30), Window(BreakKind.Break, 9, 15)],
        };
        var alsoWithLunch = new Shift
        {
            Name = "B", StartTime = new TimeOnly(15, 0), EndTime = new TimeOnly(23, 0),
            ScheduledBreaks = [Window(BreakKind.Lunch, 19, 30), Window(BreakKind.Break, 21, 15)],
        };
        var noLunch = new Shift { Name = "C", StartTime = new TimeOnly(15, 0), EndTime = new TimeOnly(19, 0) };

        // The covering employee's own lunch is the one that's kept.
        var combined = ShiftAssignmentsController.BuildCombinedShift(withLunch, alsoWithLunch);
        Assert.Equal(new TimeOnly(11, 0), Assert.Single(combined.ScheduledBreaks, b => b.Kind == BreakKind.Lunch).StartTime);
        Assert.Equal(2, combined.ScheduledBreaks.Count(b => b.Kind == BreakKind.Break));

        // With no lunch of their own, they get the covered shift's.
        var borrowed = ShiftAssignmentsController.BuildCombinedShift(noLunch, withLunch);
        Assert.Equal(new TimeOnly(11, 0), Assert.Single(borrowed.ScheduledBreaks, b => b.Kind == BreakKind.Lunch).StartTime);
    }

    [Fact]
    public async Task Cover_can_be_assigned_later_but_only_on_an_absent_shift()
    {
        var absent = Assign(morning, sam);

        var tooEarly = await As(lead).AssignCover(absent.Id, new AssignCoverRequest(alex.Id, ConfirmUnavailable: true));
        Assert.IsType<BadRequestObjectResult>(tooEarly.Result);

        Ok(await As(lead).CallOut(absent.Id, CallOutWith(null)));
        var result = Ok(await As(lead).AssignCover(absent.Id, new AssignCoverRequest(alex.Id, ConfirmUnavailable: true)));

        Assert.Equal(alex.Id, result.Cover!.AccountId);
        Assert.Equal(lead.Id, Reload(result.Cover.Id).CoverAssignedByAccountId);
    }

    [Fact]
    public async Task Changing_the_cover_employee_replaces_the_old_cover_shift()
    {
        var absent = Assign(morning, sam);
        var jo = Seed("jo", AccountRole.Employee, location);
        var first = Ok(await As(admin).CallOut(absent.Id, CallOutWith(alex))).Cover!;

        var second = Ok(await As(admin).AssignCover(absent.Id, new AssignCoverRequest(jo.Id, ConfirmUnavailable: true)));

        Assert.Equal(jo.Id, second.Cover!.AccountId);
        Assert.Equal("jo", second.Absent.CoveredByAccountFirstName);
        Assert.Null(NewContext().ShiftAssignments.Find(first.Id));
        Assert.Equal(jo.Id, CoverOf(absent)!.AccountId);
    }

    [Fact]
    public async Task Cover_cannot_be_changed_or_removed_once_the_cover_employee_has_clocked_in()
    {
        var absent = Assign(morning, sam);
        var jo = Seed("jo", AccountRole.Employee, location);
        var cover = Ok(await As(admin).CallOut(absent.Id, CallOutWith(alex))).Cover!;
        db.TimeEntries.Add(new TimeEntry { AccountId = alex.Id, ShiftAssignmentId = cover.Id, ClockInAt = DateTime.UtcNow });
        db.SaveChanges();

        var replace = await As(admin).AssignCover(absent.Id, new AssignCoverRequest(jo.Id, ConfirmUnavailable: true));
        var remove = await As(admin).RemoveCover(absent.Id);

        Assert.IsType<ConflictObjectResult>(replace.Result);
        Assert.IsType<ConflictObjectResult>(remove.Result);
        Assert.Equal(alex.Id, CoverOf(absent)!.AccountId);
    }

    [Fact]
    public async Task Removing_cover_deletes_the_cover_shift_and_leaves_the_absence()
    {
        var absent = Assign(morning, sam);
        Ok(await As(admin).CallOut(absent.Id, CallOutWith(alex)));

        var result = Ok(await As(lead).RemoveCover(absent.Id));

        Assert.True(result.IsAbsent);
        Assert.Null(result.CoveredByAssignmentId);
        Assert.Null(CoverOf(absent));
    }

    [Fact]
    public async Task Clearing_the_absence_leaves_the_cover_shift_in_place()
    {
        var absent = Assign(morning, sam);
        Ok(await As(admin).CallOut(absent.Id, CallOutWith(alex)));

        Ok(await As(admin).MarkAbsent(absent.Id, new MarkAbsentRequest(false, null)));

        Assert.False(Reload(absent.Id).IsAbsent);
        Assert.Equal(alex.Id, CoverOf(absent)!.AccountId);
    }

    [Fact]
    public async Task The_original_employee_clocking_in_leaves_the_cover_shift_in_place()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var started = SeedShift("Started", 0, 0, lunchAt: 0);
        started.EndTime = new TimeOnly(23, 59);
        db.SaveChanges();
        var absent = Assign(started, sam, today);
        Ok(await As(admin).CallOut(absent.Id, CallOutWith(alex)));

        var punch = new TimeEntryPunchService(NewContext()).ClockIn(sam.Id, absent.Id, clientIp: null, bypassDeviceCheck: true);

        Assert.Equal(StatusCodes.Status200OK, punch.StatusCode);
        Assert.False(Reload(absent.Id).IsAbsent);
        Assert.Equal(alex.Id, CoverOf(absent)!.AccountId);
    }

    [Fact]
    public async Task Deleting_the_absent_shift_keeps_the_cover_shift_as_an_ordinary_one()
    {
        var absent = Assign(morning, sam);
        var cover = Ok(await As(admin).CallOut(absent.Id, CallOutWith(alex))).Cover!;

        Assert.IsType<NoContentResult>(await As(admin).Delete(absent.Id));

        var saved = Reload(cover.Id);
        Assert.Null(saved.CoversAssignmentId);
        Assert.Equal(alex.Id, saved.AccountId);
    }

    [Fact]
    public async Task A_lead_can_assign_cover_but_not_record_sick_hours()
    {
        var absent = Assign(morning, sam);

        var withSick = await As(lead).CallOut(absent.Id, CallOutWith(alex, sickMinutes: 120));
        Assert.IsType<ForbidResult>(withSick.Result);
        Assert.False(Reload(absent.Id).IsAbsent);

        var without = Ok(await As(lead).CallOut(absent.Id, CallOutWith(alex)));
        Assert.Equal(alex.Id, without.Cover!.AccountId);
        Assert.Equal(0, without.Absent.SickMinutes);
    }

    [Fact]
    public async Task Another_locations_admin_cannot_touch_the_shift()
    {
        var absent = Assign(morning, sam);
        var outsider = Seed("outsider", AccountRole.Admin, otherLocation);

        Assert.IsType<NotFoundResult>((await As(outsider).CallOut(absent.Id, CallOutWith(alex))).Result);
        Assert.IsType<NotFoundResult>((await As(outsider).AssignCover(absent.Id, new AssignCoverRequest(alex.Id))).Result);
        Assert.IsType<NotFoundResult>((await As(outsider).RemoveCover(absent.Id)).Result);
        Assert.IsType<NotFoundResult>(As(outsider).GetCoverCandidates(absent.Id).Result);
    }

    [Fact]
    public void The_cover_routes_are_open_to_leads_and_closed_to_employees()
    {
        foreach (var action in new[]
        {
            nameof(ShiftAssignmentsController.GetCoverCandidates), nameof(ShiftAssignmentsController.CallOut),
            nameof(ShiftAssignmentsController.AssignCover), nameof(ShiftAssignmentsController.RemoveCover),
        })
        {
            var authorize = typeof(ShiftAssignmentsController).GetMethod(action)!
                .GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), false)
                .Cast<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>()
                .Single();
            Assert.Equal("LeadOrAbove", authorize.Policy);
        }
    }

    [Fact]
    public async Task The_cover_employee_is_emailed_when_asked_and_smtp_is_set_up()
    {
        var absent = Assign(morning, sam);
        var pat = Seed("pat", AccountRole.Employee, location, email: "pat@example.test");

        // No SMTP yet: the cover still goes through, just without an email.
        var noSmtp = Ok(await As(admin).CallOut(absent.Id, CallOutWith(pat, sendEmail: true)));
        Assert.False(noSmtp.EmailSent);
        Assert.Empty(emails.Sent);
        Ok(await As(admin).RemoveCover(absent.Id));

        db.LocationSettings.Add(new LocationSettings { LocationId = location.Id, SmtpHost = "smtp.example.test" });
        db.SaveChanges();

        var sent = Ok(await As(admin).AssignCover(absent.Id, new AssignCoverRequest(pat.Id, ConfirmUnavailable: true, SendEmail: true)));

        Assert.True(sent.EmailSent);
        var email = Assert.Single(emails.Sent);
        Assert.Equal("pat@example.test", email.To);
        Assert.Contains("Mon, Sep 14", email.Subject);
        Assert.Contains("sam Tester", email.BodyHtml);
        Assert.Contains("Morning", email.BodyHtml);
        Assert.Contains("7:00 AM–3:00 PM", email.BodyHtml);

        // Asked for, but alex has no address on file.
        var tuesday = Assign(morning, sam, Monday.AddDays(1));
        var noAddress = Ok(await As(admin).CallOut(tuesday.Id, CallOutWith(alex, sendEmail: true)));
        Assert.False(noAddress.EmailSent);
        Assert.Single(emails.Sent);
    }

    [Fact]
    public async Task Candidates_show_availability_and_the_combined_shift_for_people_already_working()
    {
        var absent = Assign(evening, sam);
        var available = Seed("available", AccountRole.Employee, location, email: "a@example.test");
        MakeAvailable(available);
        var working = Seed("working", AccountRole.Employee, location);
        Assign(morning, working);
        var gone = Seed("gone", AccountRole.Employee, location);
        var goneShift = Assign(morning, gone);
        db.TimeEntries.Add(new TimeEntry
        {
            AccountId = gone.Id, ShiftAssignmentId = goneShift.Id, ClockInAt = DateTime.UtcNow.AddHours(-8), ClockOutAt = DateTime.UtcNow,
        });
        db.SaveChanges();
        var calledOut = Seed("calledout", AccountRole.Employee, location);
        Ok(await As(admin).CallOut(Assign(morning, calledOut).Id, CallOutWith(null)));
        Seed("inactive", AccountRole.Employee, location, active: false);
        Seed("elsewhere", AccountRole.Employee, otherLocation);

        var result = Ok(As(lead).GetCoverCandidates(absent.Id));
        var byName = result.Candidates.ToDictionary(c => c.FirstName);

        // Not offered: the absent employee, someone who called out that day,
        // inactive accounts and other locations.
        Assert.Equal(
            ["admin", "alex", "available", "gone", "lead", "working"],
            byName.Keys.Order().ToList());

        Assert.False(result.EmailConfigured);
        Assert.Null(result.CurrentCoverAccountId);

        Assert.True(byName["available"].IsAvailable);
        Assert.True(byName["available"].HasEmail);
        Assert.Null(byName["available"].OwnShift);
        Assert.Null(byName["available"].CombinedShift);
        Assert.Null(byName["available"].BlockedReason);
        Assert.Equal(7.5, byName["available"].WeekScheduledHours);
        Assert.Equal(0, byName["available"].OvertimeMinutes);

        Assert.False(byName["alex"].IsAvailable);
        Assert.False(byName["alex"].HasEmail);

        // Their 7.5h morning becomes one 15.5h day: 7.5h past the default daily 8.
        var candidate = byName["working"];
        Assert.Equal("Morning", candidate.OwnShift!.ShiftName);
        Assert.Equal(7.5, candidate.OwnShift.Hours);
        Assert.Equal("Morning + Evening", candidate.CombinedShift!.ShiftName);
        Assert.Equal(new TimeOnly(7, 0), candidate.CombinedShift.StartTime);
        Assert.Equal(new TimeOnly(23, 0), candidate.CombinedShift.EndTime);
        Assert.Equal(15.5, candidate.CombinedShift.Hours);
        Assert.Single(candidate.CombinedShift.ScheduledBreaks, b => b.Kind == BreakKind.Lunch);
        Assert.Equal(0, candidate.BridgedGapMinutes);
        Assert.Null(candidate.BlockedReason);
        Assert.Equal(15.5, candidate.WeekScheduledHours);
        Assert.Equal(450, candidate.OvertimeMinutes);

        Assert.NotNull(byName["gone"].BlockedReason);
        Assert.Null(byName["gone"].CombinedShift);
    }

    [Fact]
    public async Task Candidate_hours_count_the_rest_of_the_workweek_and_the_current_cover_only_once()
    {
        db.LocationSettings.Add(new LocationSettings
        {
            LocationId = location.Id,
            OvertimePreset = OvertimePreset.Federal,
            OvertimeDailyThresholdMinutes = null,
            WeeklyOvertimeAfterMinutes = 40 * 60,
            SmtpHost = "smtp.example.test",
        });
        db.SaveChanges();

        // Alex already has Mon–Fri mornings (37.5h); covering Saturday makes 45h.
        for (var offset = 0; offset < 5; offset++)
        {
            Assign(morning, alex, Monday.AddDays(offset));
        }

        var absent = Assign(morning, sam, Monday.AddDays(5));
        var before = Ok(As(admin).GetCoverCandidates(absent.Id)).Candidates.Single(c => c.AccountId == alex.Id);
        Assert.Equal(45, before.WeekScheduledHours);
        Assert.Equal(5 * 60, before.OvertimeMinutes);

        Ok(await As(admin).CallOut(absent.Id, CallOutWith(alex)));
        var after = Ok(As(admin).GetCoverCandidates(absent.Id));
        Assert.True(after.EmailConfigured);
        Assert.Equal(alex.Id, after.CurrentCoverAccountId);
        Assert.Equal(45, after.Candidates.Single(c => c.AccountId == alex.Id).WeekScheduledHours);

        // A combined day is counted once too, and still shows what it replaced.
        var friday = Assign(evening, sam, Monday.AddDays(4));
        Ok(await As(admin).CallOut(friday.Id, CallOutWith(alex, confirmCombine: true)));
        var combined = Ok(As(admin).GetCoverCandidates(friday.Id)).Candidates.Single(c => c.AccountId == alex.Id);
        Assert.Equal(53, combined.WeekScheduledHours);
        Assert.Equal("Morning", combined.OwnShift!.ShiftName);
        Assert.Equal("Morning + Evening", combined.CombinedShift!.ShiftName);
        Assert.Null(combined.BlockedReason);
    }
}
