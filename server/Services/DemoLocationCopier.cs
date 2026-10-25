using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Server.Data;
using Server.Dtos;
using Server.Models;
using Server.Security;

namespace Server.Services;

// Builds the "demo" location used to show the product to prospects as an
// anonymized copy of a real location: its settings, shifts, schedule,
// punches, call-outs, sick time and availability, but with every person
// given a made-up name and email. Birth dates, addresses, phone numbers,
// SSNs, photos, documents and write-ups are left out, pay rates are nudged,
// email is turned off, and the punch-device IP restriction is dropped so
// the kiosk works from wherever the demo is given. Changes made during a
// demo never touch the real location, and running this again throws the
// old copy away first, so the demo can be refreshed right before it's shown.
//
// Only ever deletes the location whose code is exactly DemoLocationCode.
public class DemoLocationCopier(AppDbContext db, PinProtector pinProtector)
{
    public const string DemoLocationCode = "demo";

    private static readonly string[] FirstNames =
    [
        "Avery", "Jordan", "Taylor", "Morgan", "Riley", "Casey", "Jamie", "Quinn", "Skyler", "Reese",
        "Harper", "Rowan", "Emerson", "Finley", "Hayden", "Kendall", "Logan", "Parker", "Sawyer", "Blake",
        "Cameron", "Dakota", "Elliot", "Frankie", "Gray", "Jesse", "Kai", "Lane", "Marley", "Noel",
    ];

    private static readonly string[] LastNames =
    [
        "Bennett", "Carter", "Dalton", "Ellis", "Foster", "Garrison", "Hayes", "Irving", "Jensen", "Keller",
        "Lawson", "Mercer", "Norris", "Oakley", "Preston", "Quincy", "Reyes", "Sutton", "Turner", "Vaughn",
        "Walsh", "Yates", "Abbott", "Brennan", "Crawford", "Donovan", "Easton", "Fletcher", "Gibson", "Holland",
    ];

    public DemoLocationResult CopyFrom(string sourceLocationCode, string adminPassword, string kioskPasscode)
    {
        if (sourceLocationCode == DemoLocationCode)
        {
            throw new InvalidOperationException("The demo location can't be copied onto itself.");
        }

        var source = db.Locations.AsNoTracking().SingleOrDefault(l => l.LocationCode == sourceLocationCode)
            ?? throw new KeyNotFoundException($"No location with code '{sourceLocationCode}'.");

        using var tx = db.Database.BeginTransaction();
        DeleteExisting();
        var result = Copy(source, adminPassword, kioskPasscode);
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
        // Write-ups and documents are never copied in, but may have been
        // added during a demo.
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

    private DemoLocationResult Copy(Location source, string adminPassword, string kioskPasscode)
    {
        var location = new Location
        {
            Name = "Demo",
            Address = "100 Main St, San Jose, CA 95113",
            LocationCode = DemoLocationCode,
            Phone = "(408) 555-0100",
            Email = "office@demo.example.com",
        };
        db.Locations.Add(location);
        db.SaveChanges();

        // Settings, minus email and the punch-device IP restriction.
        var settings = db.LocationSettings.AsNoTracking().SingleOrDefault(s => s.LocationId == source.Id) ?? new LocationSettings();
        settings.Id = 0;
        settings.LocationId = location.Id;
        settings.SmtpHost = null;
        settings.SmtpPort = null;
        settings.SmtpUsername = null;
        settings.SmtpPassword = null;
        settings.SmtpFromAddress = null;
        settings.SmtpFromName = null;
        settings.ClockInAnywhere = true;
        settings.KioskPasscodeHash = PasswordHasher.Hash(kioskPasscode);
        db.LocationSettings.Add(settings);

        // Same random sequence every run, so a reset gives everyone the same
        // made-up name and the cheat sheet stays valid.
        var random = new Random(SourceSeed(source.LocationCode));
        var sourceAccounts = db.Accounts.AsNoTracking()
            .Where(a => a.LocationId == source.Id)
            .OrderBy(a => a.Id)
            .ToList();
        var fakeNames = MadeUpNames(sourceAccounts.Count, random);

        // Real first/last names -> made-up ones, for scrubbing free text.
        var nameSwaps = new List<(string Real, string Fake)>();

        foreach (var template in db.EmailTemplates.AsNoTracking().Where(t => t.LocationId == source.Id).ToList())
        {
            template.Id = 0;
            template.LocationId = location.Id;
            db.EmailTemplates.Add(template);
        }

        var shiftMap = new Dictionary<int, Shift>();
        foreach (var shift in db.Shifts.AsNoTracking().Include(s => s.ScheduledBreaks).Where(s => s.LocationId == source.Id).ToList())
        {
            shiftMap[shift.Id] = shift;
            shift.Id = 0;
            shift.LocationId = location.Id;
            foreach (var b in shift.ScheduledBreaks)
            {
                b.Id = 0;
                b.ShiftId = 0;
            }
            db.Shifts.Add(shift);
        }

        // Hashing is slow on purpose, so hash once: admins and leads share
        // the password the Super Admin chose, and staff sign in with their
        // code, never a password, so they share an unguessable one.
        var adminPasswordHash = PasswordHasher.Hash(adminPassword);
        var unusedPasswordHash = PasswordHasher.Hash(Guid.NewGuid().ToString("N"));

        var accountMap = new Dictionary<int, Account>();
        for (var i = 0; i < sourceAccounts.Count; i++)
        {
            var real = sourceAccounts[i];
            var (first, last) = fakeNames[i];
            nameSwaps.Add((real.FirstName, first));
            nameSwaps.Add((real.LastName, last));

            var signsInWithPassword = real.Role is AccountRole.Admin or AccountRole.Lead;
            var copy = new Account
            {
                Username = signsInWithPassword
                    ? UniqueUsername($"demo.{first}.{last}".ToLowerInvariant())
                    : AccountProvisioning.GenerateUniqueUsername(db, first, last),
                PasswordHash = signsInWithPassword ? adminPasswordHash : unusedPasswordHash,
                FirstName = first,
                LastName = last,
                Email = $"{first}.{last}@demo.example.com".ToLowerInvariant(),
                Phone = string.Empty,
                Role = real.Role,
                IsActive = real.IsActive,
                IsOnShiftSchedule = real.IsOnShiftSchedule,
                CanSeeAllSchedules = real.CanSeeAllSchedules,
                CanWriteUpOthers = real.CanWriteUpOthers,
                CanManageOthersSchedules = real.CanManageOthersSchedules,
                CreatedAt = real.CreatedAt,
                // Codes are unique only within a location, so the real ones
                // can be reused as-is in the new one.
                UserCode = real.UserCode,
                LocationId = location.Id,
                JobTitle = real.JobTitle,
                AdpStatus = real.AdpStatus,
                HourlyRate = real.HourlyRate is { } rate ? Math.Round(rate * (decimal)(0.95 + random.NextDouble() * 0.1), 2) : null,
                HireDate = real.HireDate,
                EmploymentType = real.EmploymentType,
                IsOvertimeExempt = real.IsOvertimeExempt,
                PinEncrypted = real.PinEncrypted,
            };
            accountMap[real.Id] = copy;
            db.Accounts.Add(copy);
        }
        db.SaveChanges();

        // Longest first, so "Ann" never eats part of "Anna".
        var swaps = nameSwaps
            .Where(s => s.Real.Trim().Length >= 3)
            .DistinctBy(s => s.Real.Trim().ToLowerInvariant())
            .OrderByDescending(s => s.Real.Trim().Length)
            .ToList();
        string? Scrub(string? text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }
            foreach (var (realName, fake) in swaps)
            {
                text = Regex.Replace(text, $@"\b{Regex.Escape(realName.Trim())}\b", fake, RegexOptions.IgnoreCase);
            }
            return text;
        }

        // Accounts outside the location (the Sa) keep their id.
        int Acct(int id) => accountMap.TryGetValue(id, out var a) ? a.Id : id;
        int? AcctOrNull(int? id) => id is { } v ? Acct(v) : null;
        var oldAccountIds = accountMap.Keys.ToList();

        foreach (var period in db.EmploymentPeriods.AsNoTracking().Where(p => oldAccountIds.Contains(p.AccountId)).ToList())
        {
            period.Id = 0;
            period.AccountId = Acct(period.AccountId);
            period.Notes = Scrub(period.Notes);
            db.EmploymentPeriods.Add(period);
        }

        foreach (var availability in db.Availabilities.AsNoTracking().Include(a => a.Days).Where(a => oldAccountIds.Contains(a.AccountId)).ToList())
        {
            availability.Id = 0;
            availability.AccountId = Acct(availability.AccountId);
            foreach (var day in availability.Days)
            {
                day.Id = 0;
                day.AvailabilityId = 0;
            }
            db.Availabilities.Add(availability);
        }

        foreach (var sick in db.SickTimeEntries.AsNoTracking().Where(e => oldAccountIds.Contains(e.AccountId)).ToList())
        {
            sick.Id = 0;
            sick.AccountId = Acct(sick.AccountId);
            sick.RecordedByAccountId = Acct(sick.RecordedByAccountId);
            sick.Note = Scrub(sick.Note);
            db.SickTimeEntries.Add(sick);
        }

        // Shift assignments. Covers point at other assignments, so those
        // links are filled in once every assignment has its new id.
        var sourceAssignments = db.ShiftAssignments.AsNoTracking().Where(a => oldAccountIds.Contains(a.AccountId)).ToList();
        var assignmentMap = new Dictionary<int, ShiftAssignment>();
        var covers = new List<(ShiftAssignment Assignment, int OldCoversId)>();
        foreach (var a in sourceAssignments)
        {
            if (!shiftMap.TryGetValue(a.ShiftId, out var shift))
            {
                continue;
            }
            assignmentMap[a.Id] = a;
            if (a.CoversAssignmentId is { } coversId)
            {
                covers.Add((a, coversId));
            }
            a.Id = 0;
            a.CoversAssignmentId = null;
            a.Shift = shift;
            a.ShiftId = 0;
            a.OriginalShift = a.OriginalShiftId is { } originalId && shiftMap.TryGetValue(originalId, out var original) ? original : null;
            a.OriginalShiftId = null;
            a.AccountId = Acct(a.AccountId);
            a.AbsenceNote = Scrub(a.AbsenceNote);
            a.AbsentMarkedByAccountId = AcctOrNull(a.AbsentMarkedByAccountId);
            a.SickHoursRecordedByAccountId = AcctOrNull(a.SickHoursRecordedByAccountId);
            a.CoverAssignedByAccountId = AcctOrNull(a.CoverAssignedByAccountId);
            db.ShiftAssignments.Add(a);
        }
        db.SaveChanges();

        foreach (var (assignment, oldCoversId) in covers)
        {
            if (assignmentMap.TryGetValue(oldCoversId, out var covered))
            {
                assignment.CoversAssignmentId = covered.Id;
            }
        }

        foreach (var entry in db.TimeEntries.AsNoTracking().Include(t => t.Segments).Where(t => oldAccountIds.Contains(t.AccountId)).ToList())
        {
            if (!assignmentMap.TryGetValue(entry.ShiftAssignmentId, out var assignment))
            {
                continue;
            }
            entry.Id = 0;
            entry.AccountId = Acct(entry.AccountId);
            entry.ShiftAssignmentId = assignment.Id;
            entry.ClockedOutByAccountId = AcctOrNull(entry.ClockedOutByAccountId);
            entry.LeftEarlyMarkedByAccountId = AcctOrNull(entry.LeftEarlyMarkedByAccountId);
            entry.EditedByAccountId = AcctOrNull(entry.EditedByAccountId);
            entry.Note = Scrub(entry.Note);
            entry.LeftEarlyNote = Scrub(entry.LeftEarlyNote);
            foreach (var segment in entry.Segments)
            {
                segment.Id = 0;
                segment.TimeEntryId = 0;
            }
            db.TimeEntries.Add(entry);
        }

        db.SaveChanges();

        var logins = accountMap.Values
            .OrderBy(a => a.Role)
            .ThenBy(a => a.LastName)
            .ThenBy(a => a.FirstName)
            .Where(a => a.IsActive)
            .Select(a => new DemoLoginDto(
                $"{a.FirstName} {a.LastName}",
                a.JobTitle ?? string.Empty,
                a.Role.ToString(),
                a.Role is AccountRole.Admin or AccountRole.Lead ? a.Username : null,
                a.UserCode,
                a.PinEncrypted is null ? null : pinProtector.Unprotect(a.PinEncrypted)))
            .ToList();

        return new DemoLocationResult(
            new LocationDto(location.Id, location.Name, location.Address, location.LocationCode, location.Phone, location.Email, location.IsActive),
            logins);
    }

    // Every first/last combination, shuffled, so no two people share a name.
    private static List<(string First, string Last)> MadeUpNames(int count, Random random)
    {
        var all = FirstNames.SelectMany(f => LastNames.Select(l => (f, l))).ToList();
        random.Shuffle(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(all));
        return Enumerable.Range(0, count).Select(i => all[i % all.Count]).ToList();
    }

    private static int SourceSeed(string code) => code.Aggregate(17, (h, c) => h * 31 + c);

    private string UniqueUsername(string wanted)
    {
        var username = wanted;
        var suffix = 1;
        while (db.Accounts.Local.Any(a => a.Username == username) || db.Accounts.Any(a => a.Username == username))
        {
            username = $"{wanted}{++suffix}";
        }
        return username;
    }
}
