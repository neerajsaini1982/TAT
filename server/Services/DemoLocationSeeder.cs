using Microsoft.EntityFrameworkCore;
using Server.Data;
using Server.Dtos;
using Server.Models;
using Server.Security;

namespace Server.Services;

// Builds (or rebuilds) the "demo" location used to show the product to
// prospects: a small medical clinic with staff, shift templates, a published
// schedule from two weeks back through the end of next week, and realistic
// punches up to "now" — a late arrival, a long lunch, overtime, a call-out
// with cover, a no-show, a left-early and a couple of punches to fix. Every
// date is relative to the day it runs, so reset it shortly before a demo.
//
// Reset deletes everything belonging to the location whose code is exactly
// DemoLocationCode and nothing else. Never point this at a real location.
public class DemoLocationSeeder(AppDbContext db, PinProtector pinProtector)
{
    public const string DemoLocationCode = "demo";
    private const string TimeZoneId = "America/Los_Angeles";

    private sealed record ShiftSpec(string Name, int StartH, int StartM, int EndH, int EndM, (BreakKind Kind, int H, int M, int Minutes)[] Breaks);

    private static readonly ShiftSpec FrontDesk = new("Front Desk", 7, 30, 16, 0,
        [(BreakKind.Break, 9, 45, 10), (BreakKind.Lunch, 12, 0, 30), (BreakKind.Break, 14, 30, 10)]);
    private static readonly ShiftSpec Clinical = new("Clinical", 8, 0, 16, 30,
        [(BreakKind.Break, 10, 0, 10), (BreakKind.Lunch, 12, 30, 30), (BreakKind.Break, 15, 0, 10)]);
    private static readonly ShiftSpec LateClinic = new("Late Clinic", 10, 30, 19, 0,
        [(BreakKind.Break, 12, 30, 10), (BreakKind.Lunch, 15, 0, 30), (BreakKind.Break, 17, 15, 10)]);
    private static readonly ShiftSpec Saturday = new("Saturday Clinic", 8, 0, 13, 0,
        [(BreakKind.Break, 10, 30, 10)]);

    private sealed record StaffSpec(string First, string Last, string JobTitle, AccountRole Role, EmploymentType Type, decimal Rate, ShiftSpec Shift, DayOfWeek[] Days, int SaturdayGroup);

    private static readonly DayOfWeek[] Weekdays = [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday];
    private static readonly DayOfWeek[] MonWedFri = [DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday];
    private static readonly DayOfWeek[] TueThu = [DayOfWeek.Tuesday, DayOfWeek.Thursday];

    // SaturdayGroup 1 or 2 alternates Saturdays week by week (0 = never), so
    // those weeks run past 40 hours and the payroll report shows weekly
    // overtime.
    private static readonly StaffSpec[] Staff =
    [
        new("Priya", "Patel", "Front Desk Lead", AccountRole.Lead, EmploymentType.FullTime, 27.50m, FrontDesk, Weekdays, 0),
        new("Jessica", "Nguyen", "Front Desk Receptionist", AccountRole.Employee, EmploymentType.FullTime, 21.00m, FrontDesk, Weekdays, 1),
        new("Daniel", "Kim", "Front Desk Receptionist", AccountRole.Employee, EmploymentType.FullTime, 21.00m, LateClinic, Weekdays, 2),
        new("Ashley", "Brooks", "Patient Coordinator", AccountRole.Employee, EmploymentType.FullTime, 22.50m, Clinical, Weekdays, 0),
        new("Carlos", "Ramirez", "Medical Assistant", AccountRole.Employee, EmploymentType.FullTime, 23.00m, Clinical, Weekdays, 1),
        new("Emily", "Chen", "Medical Assistant", AccountRole.Employee, EmploymentType.FullTime, 23.00m, Clinical, Weekdays, 2),
        new("Brandon", "Lee", "Medical Assistant", AccountRole.Employee, EmploymentType.FullTime, 22.00m, LateClinic, Weekdays, 0),
        new("Sofia", "Martinez", "Medical Assistant", AccountRole.Employee, EmploymentType.PartTime, 22.00m, Clinical, MonWedFri, 0),
        new("Rachel", "Adams", "Registered Nurse", AccountRole.Employee, EmploymentType.FullTime, 48.00m, Clinical, Weekdays, 0),
        new("Kevin", "Wright", "LVN", AccountRole.Employee, EmploymentType.FullTime, 34.00m, LateClinic, Weekdays, 0),
        new("Linda", "Park", "Billing Specialist", AccountRole.Employee, EmploymentType.PartTime, 26.00m, FrontDesk, MonWedFri, 0),
        new("Omar", "Haddad", "X-Ray Technician", AccountRole.Employee, EmploymentType.PartTime, 31.00m, Clinical, TueThu, 0),
    ];

    public DemoLocationResult Reset(string adminPassword, string kioskPasscode)
    {
        using var tx = db.Database.BeginTransaction();
        DeleteExisting();
        var result = Create(adminPassword, kioskPasscode);
        tx.Commit();
        return result;
    }

    private void DeleteExisting()
    {
        var location = db.Locations.SingleOrDefault(l => l.LocationCode == DemoLocationCode);
        if (location is null)
        {
            return;
        }

        var id = location.Id;
        var accountIds = db.Accounts.Where(a => a.LocationId == id).Select(a => a.Id).ToList();
        var writeUpIds = db.WriteUps
            .Where(w => accountIds.Contains(w.AccountId) || accountIds.Contains(w.CreatedByAccountId))
            .Select(w => w.Id)
            .ToList();

        // Children before parents, since most of these foreign keys restrict.
        db.WriteUpEvents.Where(e => writeUpIds.Contains(e.WriteUpId)).ExecuteDelete();
        db.WriteUpSignatures.Where(s => writeUpIds.Contains(s.WriteUpId)).ExecuteDelete();
        db.WriteUps.Where(w => writeUpIds.Contains(w.Id)).ExecuteDelete();
        db.SickTimeEntries.Where(e => accountIds.Contains(e.AccountId)).ExecuteDelete();
        db.TimeEntrySegments.Where(s => accountIds.Contains(s.TimeEntry!.AccountId)).ExecuteDelete();
        db.TimeEntries.Where(t => accountIds.Contains(t.AccountId)).ExecuteDelete();
        db.ShiftAssignments.Where(a => accountIds.Contains(a.AccountId)).ExecuteDelete();
        db.EmployeeDocuments.Where(d => accountIds.Contains(d.AccountId)).ExecuteDelete();
        db.EmploymentPeriods.Where(p => accountIds.Contains(p.AccountId)).ExecuteDelete();
        db.AvailabilityDays.Where(d => accountIds.Contains(d.Availability!.AccountId)).ExecuteDelete();
        db.Availabilities.Where(a => accountIds.Contains(a.AccountId)).ExecuteDelete();
        db.Accounts.Where(a => a.LocationId == id).ExecuteDelete();
        db.ScheduledBreaks.Where(b => b.Shift!.LocationId == id).ExecuteDelete();
        db.Shifts.Where(s => s.LocationId == id).ExecuteDelete();
        db.LocationSettings.Where(s => s.LocationId == id).ExecuteDelete();
        db.EmailTemplates.Where(t => t.LocationId == id).ExecuteDelete();
        db.AllowedPunchDevices.Where(d => d.LocationId == id).ExecuteDelete();
        db.Locations.Where(l => l.Id == id).ExecuteDelete();
        db.ChangeTracker.Clear();
    }

    private DemoLocationResult Create(string adminPassword, string kioskPasscode)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
        var nowUtc = DateTime.UtcNow;
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, zone);
        var today = DateOnly.FromDateTime(nowLocal);
        var thisMonday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        var firstDay = thisMonday.AddDays(-14);
        var lastDay = thisMonday.AddDays(13);
        // Same seed every run, so a reset always tells the same story.
        var random = new Random(20261022);

        var location = new Location
        {
            Name = "Demo",
            Address = "1200 Health Center Dr, San Jose, CA 95128",
            LocationCode = DemoLocationCode,
            Phone = "(408) 555-0142",
            Email = "frontdesk@demo.example.com",
        };
        db.Locations.Add(location);
        db.SaveChanges();

        var policy = OvertimePolicy.ForPreset(OvertimePreset.California);
        db.LocationSettings.Add(new LocationSettings
        {
            LocationId = location.Id,
            TimeZone = TimeZoneId,
            OvertimePreset = OvertimePreset.California,
            OvertimeDailyThresholdMinutes = policy.DailyOvertimeAfterMinutes,
            DailyDoubleTimeAfterMinutes = policy.DailyDoubleTimeAfterMinutes,
            WeeklyOvertimeAfterMinutes = policy.WeeklyOvertimeAfterMinutes,
            SeventhDayDoubleTimeAfterMinutes = policy.SeventhDayDoubleTimeAfterMinutes,
            WorkweekStartDay = DayOfWeek.Monday,
            BreakLimitMinutes = 10,
            LunchLimitMinutes = 30,
            KioskPasscodeHash = PasswordHasher.Hash(kioskPasscode),
            // Biweekly, the current period starting last Monday and paid the
            // Friday after it ends.
            PayPeriodDays = 14,
            PayPeriodStartDate = thisMonday.AddDays(-7),
            PayDayStartDate = thisMonday.AddDays(11),
        });

        var shifts = new Dictionary<ShiftSpec, Shift>();
        foreach (var spec in new[] { FrontDesk, Clinical, LateClinic, Saturday })
        {
            var shift = new Shift
            {
                Name = spec.Name,
                StartTime = new TimeOnly(spec.StartH, spec.StartM),
                EndTime = new TimeOnly(spec.EndH, spec.EndM),
                LocationId = location.Id,
                ScheduledBreaks = spec.Breaks
                    .Select(b => new ScheduledBreak
                    {
                        Kind = b.Kind,
                        StartTime = new TimeOnly(b.H, b.M),
                        EndTime = new TimeOnly(b.H, b.M).AddMinutes(b.Minutes),
                    })
                    .ToList(),
            };
            shifts[spec] = shift;
            db.Shifts.Add(shift);
        }

        var admin = NewAccount(location, "Maria", "Lopez", "Practice Manager", AccountRole.Admin, EmploymentType.FullTime, 38.00m, adminPassword);
        admin.IsOnShiftSchedule = false;
        admin.IsOvertimeExempt = true;

        var staff = new List<(StaffSpec Spec, Account Account)>();
        var logins = new List<DemoLoginDto>();
        var pin = 1000;
        foreach (var spec in Staff)
        {
            var account = NewAccount(location, spec.First, spec.Last, spec.JobTitle, spec.Role, spec.Type, spec.Rate,
                spec.Role == AccountRole.Lead ? adminPassword : null);
            pin += 111;
            account.PinEncrypted = pinProtector.Protect(pin.ToString());
            staff.Add((spec, account));
        }
        db.SaveChanges();

        logins.Add(new DemoLoginDto($"{admin.FirstName} {admin.LastName}", admin.JobTitle!, "Admin", admin.Username, null, null));
        logins.AddRange(staff.Select(s => new DemoLoginDto(
            $"{s.Account.FirstName} {s.Account.LastName}",
            s.Account.JobTitle!,
            s.Account.Role.ToString(),
            s.Account.Role == AccountRole.Lead ? s.Account.Username : null,
            s.Account.UserCode,
            pinProtector.Unprotect(s.Account.PinEncrypted!))));

        // The full published schedule.
        var assignments = new List<(ShiftAssignment Assignment, ShiftSpec Spec)>();
        for (var date = firstDay; date <= lastDay; date = date.AddDays(1))
        {
            var week = (date.DayNumber - firstDay.DayNumber) / 7;
            foreach (var (spec, account) in staff)
            {
                ShiftSpec? shiftSpec = null;
                if (spec.Days.Contains(date.DayOfWeek))
                {
                    shiftSpec = spec.Shift;
                }
                else if (date.DayOfWeek == DayOfWeek.Saturday && spec.SaturdayGroup == week % 2 + 1)
                {
                    shiftSpec = Saturday;
                }
                if (shiftSpec is null)
                {
                    continue;
                }

                var assignment = new ShiftAssignment
                {
                    Shift = shifts[shiftSpec],
                    AccountId = account.Id,
                    Date = date,
                    IsPublished = true,
                    PublishedAt = nowUtc.AddDays(-21),
                };
                db.ShiftAssignments.Add(assignment);
                assignments.Add((assignment, shiftSpec));
            }
        }
        db.SaveChanges();

        Account Person(string first) => staff.Single(s => s.Account.FirstName == first).Account;
        ShiftAssignment? Find(string first, DateOnly date) =>
            assignments.FirstOrDefault(a => a.Assignment.AccountId == Person(first).Id && a.Assignment.Date == date).Assignment;

        // Past weekdays, most recent first, to hang the story on.
        var pastWeekdays = Enumerable.Range(1, 20)
            .Select(i => today.AddDays(-i))
            .Where(d => d >= firstDay && Weekdays.Contains(d.DayOfWeek))
            .ToList();
        var lastWorkday = pastWeekdays[0];

        // Call-out with cover: Ashley calls in sick, Linda (off that day) covers.
        var calloutDay = pastWeekdays.First(d => d.DayOfWeek is DayOfWeek.Tuesday or DayOfWeek.Thursday && d < lastWorkday);
        var lead = Person("Priya");
        var ashleyShift = Find("Ashley", calloutDay)!;
        ashleyShift.IsAbsent = true;
        ashleyShift.AbsenceNote = "Called out sick (fever)";
        ashleyShift.AbsentMarkedByAccountId = lead.Id;
        ashleyShift.AbsentMarkedAt = ToUtc(calloutDay, 6, 52, zone);
        ashleyShift.SickMinutes = 480;
        ashleyShift.SickHoursRecordedByAccountId = admin.Id;
        ashleyShift.SickHoursRecordedAt = ToUtc(calloutDay, 9, 15, zone);
        var cover = new ShiftAssignment
        {
            Shift = shifts[Clinical],
            AccountId = Person("Linda").Id,
            Date = calloutDay,
            IsPublished = true,
            PublishedAt = ToUtc(calloutDay, 6, 55, zone),
            CoversAssignment = ashleyShift,
            CoverAssignedByAccountId = lead.Id,
            CoverAssignedAt = ToUtc(calloutDay, 6, 55, zone),
        };
        db.ShiftAssignments.Add(cover);
        assignments.Add((cover, Clinical));

        // No-call, no-show, nobody covered.
        var noShowDay = pastWeekdays.First(d => d.DayOfWeek is DayOfWeek.Tuesday or DayOfWeek.Thursday && d < calloutDay);
        var omarShift = Find("Omar", noShowDay)!;
        omarShift.IsAbsent = true;
        omarShift.AbsenceNote = "No call, no show";
        omarShift.AbsentMarkedByAccountId = lead.Id;
        omarShift.AbsentMarkedAt = ToUtc(noShowDay, 8, 40, zone);

        db.SaveChanges();

        // Story beats on top of ordinary jittered punches.
        var lateDay = pastWeekdays[2];
        var longLunchDay = pastWeekdays[1];
        var overtimeDays = new[] { pastWeekdays[3], pastWeekdays[6] };
        var leftEarlyDay = pastWeekdays[4];

        foreach (var (assignment, spec) in assignments.OrderBy(a => a.Assignment.Date))
        {
            if (assignment.IsAbsent || assignment.Date > today)
            {
                continue;
            }

            var first = staff.Single(s => s.Account.Id == assignment.AccountId).Account.FirstName;

            // Punches to Fix: Sofia never punched and Brandon forgot to clock out.
            if (first == "Sofia" && assignment.Date == lastWorkday && lastWorkday < today)
            {
                continue;
            }

            var start = ToUtc(assignment.Date, spec.StartH, spec.StartM, zone);
            var end = ToUtc(assignment.Date, spec.EndH, spec.EndM, zone);
            if (start > nowUtc)
            {
                continue;
            }

            var clockIn = start.AddMinutes(random.Next(-7, 3));
            if (first == "Daniel" && assignment.Date == lateDay)
            {
                clockIn = start.AddMinutes(22);
            }

            var clockOut = end.AddMinutes(random.Next(-6, 3));
            if (first == "Kevin" && overtimeDays.Contains(assignment.Date))
            {
                clockOut = end.AddMinutes(105);
            }

            var entry = new TimeEntry
            {
                AccountId = assignment.AccountId,
                ShiftAssignmentId = assignment.Id,
                ClockInAt = clockIn,
            };

            var leftEarly = first == "Rachel" && assignment.Date == leftEarlyDay;
            if (leftEarly)
            {
                clockOut = ToUtc(assignment.Date, 13, 45, zone);
            }

            foreach (var b in spec.Breaks)
            {
                var segmentStart = ToUtc(assignment.Date, b.H, b.M, zone).AddMinutes(random.Next(-3, 4));
                var minutes = b.Minutes + random.Next(-1, 3);
                if (first == "Emily" && assignment.Date == longLunchDay && b.Kind == BreakKind.Lunch)
                {
                    minutes = 52;
                }
                if (segmentStart >= nowUtc || (leftEarly && segmentStart >= clockOut))
                {
                    break;
                }

                var segmentEnd = segmentStart.AddMinutes(minutes);
                entry.Segments.Add(new TimeEntrySegment
                {
                    Kind = b.Kind,
                    StartAt = segmentStart,
                    EndAt = segmentEnd <= nowUtc ? segmentEnd : null,
                });
            }

            var forgotClockOut = first == "Brandon" && assignment.Date == lastWorkday && lastWorkday < today;
            if (clockOut <= nowUtc && !forgotClockOut && entry.Segments.All(s => s.EndAt is not null))
            {
                entry.ClockOutAt = clockOut;
            }

            if (leftEarly)
            {
                entry.ClockedOutByAccountId = lead.Id;
                entry.Note = "Left for a family emergency";
                entry.LeftEarly = true;
                entry.LeftEarlyNote = "Family emergency, approved by Priya";
                entry.LeftEarlyMarkedByAccountId = lead.Id;
                entry.LeftEarlyMarkedAt = clockOut;
            }

            db.TimeEntries.Add(entry);
        }

        // Most staff have already sent next week's availability; three
        // haven't, so the dashboard has someone to chase.
        var nextMonday = thisMonday.AddDays(7);
        foreach (var (spec, account) in staff.Where(s => s.Spec.First is not ("Daniel" or "Sofia" or "Omar")))
        {
            db.Availabilities.Add(new Availability
            {
                AccountId = account.Id,
                WeekStartDate = nextMonday,
                IsSubmitted = true,
                SubmittedAt = nowUtc.AddDays(-1),
                Days = Enumerable.Range(0, 7)
                    .Select(i => nextMonday.AddDays(i))
                    .Select(d => new AvailabilityDay
                    {
                        Date = d,
                        IsAvailable = d.DayOfWeek != DayOfWeek.Sunday,
                    })
                    .ToList(),
            });
        }

        db.SaveChanges();

        return new DemoLocationResult(
            new LocationDto(location.Id, location.Name, location.Address, location.LocationCode, location.Phone, location.Email, location.IsActive),
            logins);
    }

    private Account NewAccount(Location location, string first, string last, string jobTitle, AccountRole role, EmploymentType type, decimal rate, string? password)
    {
        var username = password is null
            ? AccountProvisioning.GenerateUniqueUsername(db, first, last)
            : AccountProvisioning.GenerateUniqueUsername(db, "demo", role.ToString());
        var account = new Account
        {
            Username = username,
            PasswordHash = PasswordHasher.Hash(password ?? Guid.NewGuid().ToString("N")),
            FirstName = first,
            LastName = last,
            Email = $"{first}.{last}@demo.example.com".ToLowerInvariant(),
            Phone = "(408) 555-01" + Random.Shared.Next(10, 99),
            Role = role,
            LocationId = location.Id,
            // Fixed codes (the location is always brand new, so they can't
            // clash) so a printed cheat sheet survives a reset.
            UserCode = (100000 + db.Accounts.Local.Count(a => a.LocationId == location.Id) + 1).ToString(),
            JobTitle = jobTitle,
            EmploymentType = type,
            HourlyRate = rate,
            HireDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-Random.Shared.Next(120, 1500)),
        };
        db.Accounts.Add(account);
        return account;
    }

    private static DateTime ToUtc(DateOnly date, int hour, int minute, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(new TimeOnly(hour, minute)), zone);
}
