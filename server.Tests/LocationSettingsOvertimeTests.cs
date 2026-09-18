using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Server.Controllers;
using Server.Data;
using Server.Dtos;
using Server.Models;

namespace Server.Tests;

// The overtime part of LocationSettingsController: what gets saved, when a
// preset is downgraded to Custom, and which rule values are rejected.
public sealed class LocationSettingsOvertimeTests : IDisposable
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly AppDbContext db;

    public LocationSettingsOvertimeTests()
    {
        connection.Open();
        db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        db.Database.EnsureCreated();
        db.Locations.Add(new Location { Name = "Test", LocationCode = "t1" });
        db.SaveChanges();
    }

    public void Dispose()
    {
        db.Dispose();
        connection.Dispose();
    }

    // The email sender is only used by the test-email endpoint.
    private LocationSettingsController CreateController() => new(db, null!)
    {
        ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, nameof(AccountRole.Sa))], "test")),
            },
        },
    };

    private static UpdateLocationSettingsRequest Request(
        OvertimePreset preset,
        int? daily = null,
        int? dailyDoubleTime = null,
        int? weekly = null,
        int? seventhDay = null,
        DayOfWeek workweekStart = DayOfWeek.Monday) => new(
        TimeFormat.TwelveHour, DateFormat.MmDdYyyy, "America/Los_Angeles",
        AvailabilityDays: 7, ClockInWindowMinutes: 15, LateClockInGraceMinutes: 5, BreakLimitMinutes: 15, LunchLimitMinutes: 30,
        preset, daily, dailyDoubleTime, weekly, seventhDay, workweekStart,
        DevelopmentMode: false, ScheduleVisibilityEnabled: true, AdminSeesAllSchedules: true, LeadSeesAllSchedules: false,
        EmployeeSeesAllSchedules: false, ClockInAnywhere: true,
        SmtpHost: null, SmtpPort: null, SmtpUsername: null, SmtpPassword: null, SmtpUseSsl: true,
        SmtpFromAddress: null, SmtpFromName: null, PayDayStartDate: null, PayPeriodDays: null);

    private static UpdateLocationSettingsRequest CaliforniaRequest(DayOfWeek workweekStart = DayOfWeek.Monday, int? weekly = 2400) =>
        Request(OvertimePreset.California, daily: 480, dailyDoubleTime: 720, weekly: weekly, seventhDay: 480, workweekStart: workweekStart);

    private LocationSettingsDto Save(UpdateLocationSettingsRequest request)
    {
        var result = CreateController().Update("t1", request);
        return Assert.IsType<OkObjectResult>(result.Result).Value as LocationSettingsDto
            ?? throw new InvalidOperationException("Update did not return settings.");
    }

    private static string RejectionMessage(ActionResult<LocationSettingsDto> result) =>
        Assert.IsType<string>(Assert.IsType<BadRequestObjectResult>(result.Result).Value);

    [Fact]
    public void A_preset_with_its_own_values_is_saved_as_that_preset()
    {
        var saved = Save(CaliforniaRequest());

        Assert.Equal(OvertimePreset.California, saved.OvertimePreset);
        Assert.Equal(480, saved.OvertimeDailyThresholdMinutes);
        Assert.Equal(720, saved.DailyDoubleTimeAfterMinutes);
        Assert.Equal(2400, saved.WeeklyOvertimeAfterMinutes);
        Assert.Equal(480, saved.SeventhDayDoubleTimeAfterMinutes);
    }

    [Fact]
    public void A_preset_whose_values_were_edited_is_saved_as_custom()
    {
        var saved = Save(CaliforniaRequest(weekly: 2100));

        Assert.Equal(OvertimePreset.Custom, saved.OvertimePreset);
        Assert.Equal(2100, saved.WeeklyOvertimeAfterMinutes);
    }

    [Fact]
    public void Choosing_a_different_workweek_start_does_not_turn_a_preset_into_custom()
    {
        var saved = Save(CaliforniaRequest(workweekStart: DayOfWeek.Sunday));

        Assert.Equal(OvertimePreset.California, saved.OvertimePreset);
        Assert.Equal(DayOfWeek.Sunday, saved.WorkweekStartDay);
    }

    [Fact]
    public void Blank_rules_are_stored_as_off()
    {
        Save(CaliforniaRequest());

        var saved = Save(Request(OvertimePreset.None));

        Assert.Equal(OvertimePreset.None, saved.OvertimePreset);
        Assert.Null(saved.OvertimeDailyThresholdMinutes);
        Assert.Null(saved.DailyDoubleTimeAfterMinutes);
        Assert.Null(saved.WeeklyOvertimeAfterMinutes);
        Assert.Null(saved.SeventhDayDoubleTimeAfterMinutes);
        Assert.Equal(OvertimePolicy.None, db.LocationSettings.Single().GetOvertimePolicy());
    }

    [Fact]
    public void The_saved_rules_are_what_the_calculator_will_use()
    {
        Save(CaliforniaRequest(workweekStart: DayOfWeek.Saturday));

        Assert.Equal(
            OvertimePolicy.ForPreset(OvertimePreset.California) with { WorkweekStartDay = DayOfWeek.Saturday },
            db.LocationSettings.Single().GetOvertimePolicy());
    }

    [Theory]
    [InlineData(0, null, null, null)] // daily threshold below 1
    [InlineData(1441, null, null, null)] // daily threshold longer than a day
    [InlineData(null, 0, null, null)]
    [InlineData(null, null, 10081, null)] // weekly threshold longer than a week
    [InlineData(null, null, 0, null)]
    [InlineData(null, null, null, 1441)]
    [InlineData(480, 480, null, null)] // double time can't start when overtime does
    [InlineData(480, 400, null, null)] // ...or before it
    public void Out_of_range_or_contradictory_rules_are_rejected_and_nothing_is_saved(int? daily, int? dailyDoubleTime, int? weekly, int? seventhDay)
    {
        var result = CreateController().Update("t1", Request(OvertimePreset.Custom, daily, dailyDoubleTime, weekly, seventhDay));

        Assert.False(string.IsNullOrWhiteSpace(RejectionMessage(result)));
        Assert.Empty(db.LocationSettings);
    }

    [Fact]
    public void Double_time_with_no_daily_overtime_is_allowed()
    {
        var saved = Save(Request(OvertimePreset.Custom, dailyDoubleTime: 720));

        Assert.Null(saved.OvertimeDailyThresholdMinutes);
        Assert.Equal(720, saved.DailyDoubleTimeAfterMinutes);
    }

    [Fact]
    public void An_unknown_preset_or_workweek_day_is_rejected()
    {
        Assert.False(string.IsNullOrWhiteSpace(RejectionMessage(CreateController().Update("t1", Request((OvertimePreset)99)))));
        Assert.False(string.IsNullOrWhiteSpace(RejectionMessage(CreateController().Update("t1", Request(OvertimePreset.None, workweekStart: (DayOfWeek)9)))));
    }

    [Fact]
    public void Preset_values_are_served_from_the_server_definitions()
    {
        var presets = Assert.IsAssignableFrom<IEnumerable<OvertimePresetDto>>(
            Assert.IsType<OkObjectResult>(CreateController().GetOvertimePresets().Result).Value).ToList();

        Assert.Equal([OvertimePreset.None, OvertimePreset.Federal, OvertimePreset.California], presets.Select(p => p.Preset));

        var california = presets.Single(p => p.Preset == OvertimePreset.California);
        Assert.Equal(new OvertimePresetDto(OvertimePreset.California, 480, 720, 2400, 480), california);

        var federal = presets.Single(p => p.Preset == OvertimePreset.Federal);
        Assert.Equal(new OvertimePresetDto(OvertimePreset.Federal, null, null, 2400, null), federal);
    }
}
