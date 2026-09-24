using System.Security.Claims;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Server.Controllers;
using Server.Data;
using Server.Dtos;
using Server.Models;
using Server.Security;

namespace Server.Tests;

// Who can see and change write-ups (issue #88): an employee sees only their
// own and can acknowledge them; only an Admin/Sa of the employee's location
// (and never for their own account) can create, edit, void, record a
// declined acknowledgment, or see anyone else's. Nothing is ever deleted.
public sealed class WriteUpsControllerTests : IDisposable
{
    private static readonly DateOnly Today = new(2026, 9, 20);

    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly AppDbContext db;
    private readonly Location location;
    private readonly Location otherLocation;
    private readonly Account admin;
    private readonly Account employee;
    private readonly Account otherEmployee;

    public WriteUpsControllerTests()
    {
        connection.Open();
        db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        db.Database.EnsureCreated();

        location = new Location { Name = "Test", LocationCode = "t1" };
        otherLocation = new Location { Name = "Other", LocationCode = "t2" };
        db.Locations.AddRange(location, otherLocation);
        db.SaveChanges();

        admin = Seed("admin", AccountRole.Admin, location);
        employee = Seed("emp", AccountRole.Employee, location);
        otherEmployee = Seed("emp2", AccountRole.Employee, location);
    }

    public void Dispose()
    {
        db.Dispose();
        connection.Dispose();
    }

    private Account Seed(string username, AccountRole role, Location? at)
    {
        var account = new Account
        {
            Username = username,
            FirstName = username,
            LastName = "Tester",
            Role = role,
            LocationId = at?.Id,
        };
        db.Accounts.Add(account);
        db.SaveChanges();
        return account;
    }

    private ClaimsPrincipal Principal(Account caller)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, caller.Id.ToString()),
            new(ClaimTypes.Role, caller.Role.ToString()),
        };
        if (caller.LocationId is { } locationId)
        {
            claims.Add(new Claim(TokenService.LocationCodeClaimType, db.Locations.Find(locationId)!.LocationCode));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    private static ControllerContext ContextFor(ClaimsPrincipal user) =>
        new() { HttpContext = new DefaultHttpContext { User = user } };

    private WriteUpsController As(Account caller) => new(db) { ControllerContext = ContextFor(Principal(caller)) };

    private WriteUpDto Create(
        Account target, string description = "Late twice this week", WriteUpSeverity? severity = null,
        DateOnly? date = null, WriteUpType? type = null)
    {
        var result = As(admin).Create(target.Id, new CreateWriteUpRequest(date ?? Today, description, severity, type));
        return Assert.IsType<WriteUpDto>(Assert.IsType<CreatedAtActionResult>(result.Result).Value);
    }

    private static WriteUpDto Ok(ActionResult<WriteUpDto> result) =>
        Assert.IsType<WriteUpDto>(Assert.IsType<OkObjectResult>(result.Result).Value);

    private static List<WriteUpDto> List(ActionResult<IEnumerable<WriteUpDto>> result) =>
        Assert.IsAssignableFrom<IEnumerable<WriteUpDto>>(Assert.IsType<OkObjectResult>(result.Result).Value).ToList();

    private WriteUpDto Edit(WriteUpDto existing, string? description = null, WriteUpSeverity? severity = null, WriteUpType? type = null) =>
        Ok(As(admin).Update(
            existing.AccountId, existing.Id,
            new UpdateWriteUpRequest(existing.Date, description ?? existing.Description, severity ?? existing.Severity, type ?? existing.Type)));

    // What the employee types to acknowledge: their own full name.
    private static AcknowledgeWriteUpRequest Signed(Account who) => Ack($"{who.FirstName} {who.LastName}");

    // A request with a valid drawn signature unless a test passes its own.
    private static AcknowledgeWriteUpRequest Ack(string name, string? signature = null) =>
        new(name, signature ?? TestPng.DataUrl());

    // ---- create ---------------------------------------------------------

    [Fact]
    public void Severity_and_type_default_to_Normal_and_Written_when_omitted()
    {
        var created = Create(employee);

        Assert.Equal(WriteUpSeverity.Normal, created.Severity);
        Assert.Equal(WriteUpType.Written, created.Type);
        Assert.Equal(WriteUpSeverity.Normal, db.WriteUps.Single().Severity);
        Assert.Equal(WriteUpType.Written, db.WriteUps.Single().Type);
    }

    [Fact]
    public void Create_keeps_the_chosen_severity_type_date_and_author()
    {
        var created = Create(employee, "  No call, no show  ", WriteUpSeverity.Critical, new DateOnly(2026, 9, 1), WriteUpType.Final);

        Assert.Equal(WriteUpSeverity.Critical, created.Severity);
        Assert.Equal(WriteUpType.Final, created.Type);
        Assert.Equal(new DateOnly(2026, 9, 1), created.Date);
        Assert.Equal("No call, no show", created.Description);
        Assert.Equal(admin.Id, created.CreatedByAccountId);
        Assert.Equal("admin Tester", created.CreatedByName);
        Assert.Equal(WriteUpAcknowledgment.Pending, created.AcknowledgmentStatus);
        Assert.False(created.IsVoided);
    }

    [Fact]
    public void Severity_and_type_are_stored_by_name()
    {
        Create(employee, severity: WriteUpSeverity.High, type: WriteUpType.Verbal);

        Assert.Equal("High", db.Database.SqlQueryRaw<string>("SELECT Severity AS Value FROM WriteUps").Single());
        Assert.Equal("Verbal", db.Database.SqlQueryRaw<string>("SELECT Type AS Value FROM WriteUps").Single());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_a_blank_description(string description)
    {
        var result = As(admin).Create(employee.Id, new CreateWriteUpRequest(Today, description));

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Empty(db.WriteUps);
    }

    [Fact]
    public void Create_rejects_a_missing_date()
    {
        Assert.IsType<BadRequestObjectResult>(As(admin).Create(employee.Id, new CreateWriteUpRequest(default, "Late")).Result);
    }

    [Fact]
    public void Create_rejects_an_over_long_description()
    {
        var tooLong = new string('x', WriteUpsController.MaxDescriptionLength + 1);

        Assert.IsType<BadRequestObjectResult>(As(admin).Create(employee.Id, new CreateWriteUpRequest(Today, tooLong)).Result);
    }

    [Fact]
    public void Create_rejects_an_undefined_severity_or_type()
    {
        Assert.IsType<BadRequestObjectResult>(
            As(admin).Create(employee.Id, new CreateWriteUpRequest(Today, "Late", (WriteUpSeverity)99)).Result);
        Assert.IsType<BadRequestObjectResult>(
            As(admin).Create(employee.Id, new CreateWriteUpRequest(Today, "Late", null, (WriteUpType)99)).Result);
    }

    [Fact]
    public void Create_logs_who_created_it_in_the_history()
    {
        var created = Create(employee);

        var entry = Assert.Single(created.History!);
        Assert.Equal(WriteUpEventAction.Created, entry.Action);
        Assert.Equal(admin.Id, entry.ByAccountId);
        Assert.Equal("admin Tester", entry.ByName);
    }

    // ---- who can see what ------------------------------------------------

    [Fact]
    public void An_employee_sees_their_own_write_ups_newest_date_first_without_the_internal_details()
    {
        Create(employee, "older", date: new DateOnly(2026, 8, 1));
        Create(employee, "newer", date: new DateOnly(2026, 9, 10));
        Create(otherEmployee, "someone else's");

        var mine = List(As(employee).GetAll(employee.Id));

        Assert.Equal(["newer", "older"], mine.Select(w => w.Description));
        Assert.All(mine, w => Assert.Null(w.History));
        Assert.All(mine, w => Assert.Null(w.VoidReason));
    }

    [Fact]
    public void An_admin_sees_the_history_of_an_employees_write_ups()
    {
        Create(employee);

        var listed = Assert.Single(List(As(admin).GetAll(employee.Id)));

        Assert.NotNull(listed.History);
        Assert.Single(listed.History);
    }

    [Fact]
    public void An_employee_cannot_read_a_coworkers_write_ups()
    {
        Create(otherEmployee);

        Assert.IsType<NotFoundResult>(As(employee).GetAll(otherEmployee.Id).Result);
    }

    [Fact]
    public void A_lead_cannot_read_someone_elses_write_ups()
    {
        var lead = Seed("lead", AccountRole.Lead, location);
        Create(employee);

        Assert.IsType<NotFoundResult>(As(lead).GetAll(employee.Id).Result);
    }

    [Fact]
    public void An_admin_at_another_location_cannot_do_anything()
    {
        var outsider = Seed("outsider", AccountRole.Admin, otherLocation);
        var existing = Create(employee);

        Assert.IsType<NotFoundResult>(As(outsider).GetAll(employee.Id).Result);
        Assert.IsType<NotFoundResult>(As(outsider).Create(employee.Id, new CreateWriteUpRequest(Today, "x")).Result);
        Assert.IsType<NotFoundResult>(
            As(outsider).Update(employee.Id, existing.Id, new UpdateWriteUpRequest(Today, "x", WriteUpSeverity.Low, WriteUpType.Note)).Result);
        Assert.IsType<NotFoundResult>(As(outsider).Void(employee.Id, existing.Id, new VoidWriteUpRequest("x")).Result);
        Assert.IsType<NotFoundResult>(As(outsider).DeclineAcknowledgment(employee.Id, existing.Id).Result);
    }

    [Fact]
    public void Sa_can_manage_write_ups_at_any_location()
    {
        var sa = Seed("sa", AccountRole.Sa, null);

        Assert.IsType<CreatedAtActionResult>(As(sa).Create(employee.Id, new CreateWriteUpRequest(Today, "Late")).Result);
    }

    [Fact]
    public void An_employee_cannot_create_edit_void_or_decline_even_for_themselves()
    {
        var existing = Create(employee);

        Assert.IsType<NotFoundResult>(As(employee).Create(employee.Id, new CreateWriteUpRequest(Today, "x")).Result);
        Assert.IsType<NotFoundResult>(
            As(employee).Update(employee.Id, existing.Id, new UpdateWriteUpRequest(Today, "x", WriteUpSeverity.Low, WriteUpType.Note)).Result);
        Assert.IsType<NotFoundResult>(As(employee).Void(employee.Id, existing.Id, new VoidWriteUpRequest("x")).Result);
        Assert.IsType<NotFoundResult>(As(employee).DeclineAcknowledgment(employee.Id, existing.Id).Result);
        Assert.Equal("Late twice this week", db.WriteUps.Single().Description);
        Assert.False(db.WriteUps.Single().IsVoided);
    }

    [Fact]
    public void An_admin_cannot_manage_write_ups_about_themselves_but_can_see_and_acknowledge_them()
    {
        var other = Seed("admin2", AccountRole.Admin, location);
        var aboutAdmin = Ok(As(other).Create(admin.Id, new CreateWriteUpRequest(Today, "Missed the count")) is { Result: CreatedAtActionResult c }
            ? new ActionResult<WriteUpDto>(new OkObjectResult(c.Value))
            : throw new InvalidOperationException("another admin should be able to create it"));

        Assert.IsType<NotFoundResult>(As(admin).Create(admin.Id, new CreateWriteUpRequest(Today, "x")).Result);
        Assert.IsType<NotFoundResult>(
            As(admin).Update(admin.Id, aboutAdmin.Id, new UpdateWriteUpRequest(Today, "gone", WriteUpSeverity.Low, WriteUpType.Note)).Result);
        Assert.IsType<NotFoundResult>(As(admin).Void(admin.Id, aboutAdmin.Id, new VoidWriteUpRequest("x")).Result);
        Assert.IsType<NotFoundResult>(As(admin).DeclineAcknowledgment(admin.Id, aboutAdmin.Id).Result);

        // ...but they see it the way any employee would (no history)...
        var seen = Assert.Single(List(As(admin).GetAll(admin.Id)));
        Assert.Null(seen.History);

        // ...and can acknowledge it.
        Assert.Equal(WriteUpAcknowledgment.Acknowledged, Ok(As(admin).Acknowledge(admin.Id, aboutAdmin.Id, Signed(admin))).AcknowledgmentStatus);
    }

    // ---- signed in person at creation -------------------------------------

    private WriteUpDto CreateSigned(Account caller, Account target, string? employeeSignature, string? authorSignature) =>
        Assert.IsType<WriteUpDto>(Assert.IsType<CreatedAtActionResult>(
            As(caller).Create(target.Id, new CreateWriteUpRequest(Today, "Late", EmployeeSignature: employeeSignature, AuthorSignature: authorSignature)).Result).Value);

    [Fact]
    public void Signatures_are_optional_when_creating()
    {
        var created = CreateSigned(admin, employee, null, null);

        Assert.Null(created.AuthorSignatureId);
        Assert.Null(created.AcknowledgmentSignatureId);
        Assert.Equal(WriteUpAcknowledgment.Pending, created.AcknowledgmentStatus);
        Assert.Empty(db.WriteUpSignatures);
    }

    [Fact]
    public void The_author_can_sign_when_creating_and_it_is_stamped_with_their_name_and_time()
    {
        var created = CreateSigned(admin, employee, null, TestPng.DataUrl());

        var signature = db.WriteUpSignatures.Single(s => s.Id == created.AuthorSignatureId);
        Assert.Equal("admin Tester", signature.SignedName);
        Assert.Equal(created.CreatedAt, signature.SignedAt);
        Assert.Equal(WriteUpAcknowledgment.Pending, created.AcknowledgmentStatus);
        Assert.Equal(created.AuthorSignatureId, Assert.Single(List(As(employee).GetAll(employee.Id))).AuthorSignatureId);
    }

    [Fact]
    public void An_employee_signing_in_person_acknowledges_it_under_their_own_name()
    {
        var created = CreateSigned(admin, employee, TestPng.DataUrl(), TestPng.DataUrl());

        Assert.Equal(WriteUpAcknowledgment.Acknowledged, created.AcknowledgmentStatus);
        Assert.Equal("emp Tester", created.AcknowledgmentSignedName);
        Assert.NotNull(created.AcknowledgmentAt);
        Assert.NotEqual(created.AuthorSignatureId, created.AcknowledgmentSignatureId);
        Assert.Equal("emp Tester", db.WriteUpSignatures.Single(s => s.Id == created.AcknowledgmentSignatureId).SignedName);
        Assert.Contains(created.History!, e => e.Action == WriteUpEventAction.Acknowledged && e.Detail == "Signed in person: emp Tester");

        // Nothing left for the employee to do from their own portal.
        var existing = Assert.Single(List(As(employee).GetAll(employee.Id)));
        Assert.Equal(created.AcknowledgmentSignatureId, existing.AcknowledgmentSignatureId);
    }

    [Fact]
    public void A_bad_signature_rejects_the_whole_write_up()
    {
        var result = As(admin).Create(employee.Id, new CreateWriteUpRequest(Today, "Late", EmployeeSignature: "data:image/png;base64,AAAA"));

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Empty(db.WriteUps);
        Assert.Empty(db.WriteUpSignatures);
    }

    // ---- CanWriteUpOthers -------------------------------------------------

    private Account Writer(AccountRole role = AccountRole.Lead, Location? at = null)
    {
        var writer = Seed("writer", role, at ?? location);
        writer.CanWriteUpOthers = true;
        db.SaveChanges();
        return writer;
    }

    [Fact]
    public void An_account_with_CanWriteUpOthers_can_create_a_write_up_for_a_coworker_without_seeing_its_history()
    {
        var writer = Writer();

        var result = As(writer).Create(employee.Id, new CreateWriteUpRequest(Today, "Left the register open"));

        var created = Assert.IsType<WriteUpDto>(Assert.IsType<CreatedAtActionResult>(result.Result).Value);
        Assert.Null(created.History);
        Assert.Equal(writer.Id, db.WriteUps.Single().CreatedByAccountId);
    }

    [Fact]
    public void CanWriteUpOthers_grants_nothing_but_create()
    {
        var writer = Writer(AccountRole.Employee);
        var existing = Create(employee);

        Assert.IsType<NotFoundResult>(As(writer).GetAll(employee.Id).Result);
        Assert.IsType<NotFoundResult>(
            As(writer).Update(employee.Id, existing.Id, new UpdateWriteUpRequest(Today, "x", WriteUpSeverity.Low, WriteUpType.Note)).Result);
        Assert.IsType<NotFoundResult>(As(writer).Void(employee.Id, existing.Id, new VoidWriteUpRequest("x")).Result);
        Assert.IsType<NotFoundResult>(As(writer).DeclineAcknowledgment(employee.Id, existing.Id).Result);
    }

    [Fact]
    public void CanWriteUpOthers_does_not_reach_themselves_or_another_location()
    {
        var writer = Writer();
        var outsider = Seed("outsider", AccountRole.Employee, otherLocation);

        Assert.IsType<NotFoundResult>(As(writer).Create(writer.Id, new CreateWriteUpRequest(Today, "x")).Result);
        Assert.IsType<NotFoundResult>(As(writer).Create(outsider.Id, new CreateWriteUpRequest(Today, "x")).Result);
        Assert.Empty(db.WriteUps);
    }

    [Fact]
    public void Revoking_CanWriteUpOthers_takes_effect_immediately()
    {
        var writer = Writer();
        writer.CanWriteUpOthers = false;
        db.SaveChanges();

        Assert.IsType<NotFoundResult>(As(writer).Create(employee.Id, new CreateWriteUpRequest(Today, "x")).Result);
    }

    // ---- edit ------------------------------------------------------------

    [Fact]
    public void Update_changes_the_fields_and_logs_what_changed()
    {
        var existing = Create(employee);

        var updated = Ok(As(admin).Update(
            employee.Id, existing.Id,
            new UpdateWriteUpRequest(new DateOnly(2026, 9, 2), "Reworded", WriteUpSeverity.High, WriteUpType.Final)));

        Assert.Equal("Reworded", updated.Description);
        Assert.Equal(WriteUpSeverity.High, updated.Severity);
        Assert.Equal(WriteUpType.Final, updated.Type);
        Assert.Equal(new DateOnly(2026, 9, 2), updated.Date);

        var edit = updated.History!.Last();
        Assert.Equal(WriteUpEventAction.Edited, edit.Action);
        Assert.Contains("Date: 2026-09-20 → 2026-09-02", edit.Detail);
        Assert.Contains("Type: Written → Final", edit.Detail);
        Assert.Contains("Severity: Normal → High", edit.Detail);
        // The old wording stays recoverable.
        Assert.Contains("\"Late twice this week\" → \"Reworded\"", edit.Detail);
    }

    [Fact]
    public void Saving_without_changing_anything_adds_no_history()
    {
        var existing = Create(employee);

        var saved = Edit(existing);

        Assert.Single(saved.History!);
        Assert.Single(db.WriteUpEvents);
    }

    [Fact]
    public void Editing_an_acknowledged_write_up_puts_it_back_to_pending_and_says_so()
    {
        var existing = Create(employee);
        As(employee).Acknowledge(employee.Id, existing.Id, Signed(employee));

        var edited = Edit(existing, description: "Materially different");

        Assert.Equal(WriteUpAcknowledgment.Pending, edited.AcknowledgmentStatus);
        Assert.Null(edited.AcknowledgmentAt);
        Assert.Contains("Acknowledgment reset to Pending (was Acknowledged)", edited.History!.Last().Detail);
    }

    [Fact]
    public void A_voided_write_up_cannot_be_edited()
    {
        var existing = Create(employee);
        As(admin).Void(employee.Id, existing.Id, new VoidWriteUpRequest("Entered in error"));

        var result = As(admin).Update(employee.Id, existing.Id, new UpdateWriteUpRequest(Today, "x", WriteUpSeverity.Low, WriteUpType.Note));

        Assert.IsType<ConflictObjectResult>(result.Result);
        Assert.Equal("Late twice this week", db.WriteUps.Single().Description);
    }

    [Fact]
    public void Update_only_reaches_a_write_up_that_belongs_to_that_employee()
    {
        var theirs = Create(otherEmployee);

        var result = As(admin).Update(employee.Id, theirs.Id, new UpdateWriteUpRequest(Today, "x", WriteUpSeverity.Low, WriteUpType.Note));

        Assert.IsType<NotFoundResult>(result.Result);
        Assert.Equal("Late twice this week", db.WriteUps.Single().Description);
    }

    // ---- void ------------------------------------------------------------

    [Fact]
    public void Void_marks_it_voided_keeps_the_row_and_logs_the_reason()
    {
        var existing = Create(employee);

        var voided = Ok(As(admin).Void(employee.Id, existing.Id, new VoidWriteUpRequest("  Entered in error  ")));

        Assert.True(voided.IsVoided);
        Assert.NotNull(voided.VoidedAt);
        Assert.Equal("Entered in error", voided.VoidReason);
        Assert.Equal(WriteUpEventAction.Voided, voided.History!.Last().Action);
        Assert.Equal("Entered in error", voided.History.Last().Detail);
        Assert.Single(db.WriteUps);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Void_requires_a_reason(string reason)
    {
        var existing = Create(employee);

        Assert.IsType<BadRequestObjectResult>(As(admin).Void(employee.Id, existing.Id, new VoidWriteUpRequest(reason)).Result);
        Assert.False(db.WriteUps.Single().IsVoided);
    }

    [Fact]
    public void Void_rejects_an_over_long_reason()
    {
        var existing = Create(employee);
        var tooLong = new string('x', WriteUpsController.MaxVoidReasonLength + 1);

        Assert.IsType<BadRequestObjectResult>(As(admin).Void(employee.Id, existing.Id, new VoidWriteUpRequest(tooLong)).Result);
    }

    [Fact]
    public void Voiding_twice_is_a_conflict()
    {
        var existing = Create(employee);
        As(admin).Void(employee.Id, existing.Id, new VoidWriteUpRequest("Entered in error"));

        Assert.IsType<ConflictObjectResult>(As(admin).Void(employee.Id, existing.Id, new VoidWriteUpRequest("Again")).Result);
        Assert.Equal(2, db.WriteUpEvents.Count());
    }

    [Fact]
    public void The_employee_still_sees_a_voided_write_up_but_not_why()
    {
        var existing = Create(employee);
        As(admin).Void(employee.Id, existing.Id, new VoidWriteUpRequest("Internal: wrong employee"));

        var seen = Assert.Single(List(As(employee).GetAll(employee.Id)));

        Assert.True(seen.IsVoided);
        Assert.NotNull(seen.VoidedAt);
        Assert.Null(seen.VoidReason);
        Assert.Null(seen.History);
    }

    [Fact]
    public void A_voided_write_up_cannot_be_acknowledged_or_declined()
    {
        var existing = Create(employee);
        As(admin).Void(employee.Id, existing.Id, new VoidWriteUpRequest("Entered in error"));

        Assert.IsType<ConflictObjectResult>(As(employee).Acknowledge(employee.Id, existing.Id, Signed(employee)).Result);
        Assert.IsType<ConflictObjectResult>(As(admin).DeclineAcknowledgment(employee.Id, existing.Id).Result);
    }

    // ---- acknowledge / decline -------------------------------------------

    [Fact]
    public void The_employee_can_acknowledge_their_write_up()
    {
        var existing = Create(employee);

        var acknowledged = Ok(As(employee).Acknowledge(employee.Id, existing.Id, Signed(employee)));

        Assert.Equal(WriteUpAcknowledgment.Acknowledged, acknowledged.AcknowledgmentStatus);
        Assert.NotNull(acknowledged.AcknowledgmentAt);
        Assert.Null(acknowledged.History);

        var trail = Assert.Single(List(As(admin).GetAll(employee.Id))).History!;
        Assert.Equal(WriteUpEventAction.Acknowledged, trail.Last().Action);
        Assert.Equal(employee.Id, trail.Last().ByAccountId);
    }

    [Fact]
    public void Acknowledging_stores_the_typed_name_and_logs_it()
    {
        var existing = Create(employee);

        var acknowledged = Ok(As(employee).Acknowledge(employee.Id, existing.Id, Ack("emp Tester")));

        Assert.Equal("emp Tester", acknowledged.AcknowledgmentSignedName);
        var trail = Assert.Single(List(As(admin).GetAll(employee.Id))).History!;
        Assert.Equal("Signed: emp Tester", trail.Last().Detail);
    }

    [Theory]
    [InlineData("EMP TESTER", "EMP TESTER")]
    [InlineData("  emp    Tester  ", "emp Tester")]
    [InlineData("emp\tTester", "emp Tester")]
    public void The_typed_name_ignores_case_and_extra_whitespace_but_is_stored_as_typed(string typed, string stored)
    {
        var existing = Create(employee);

        var acknowledged = Ok(As(employee).Acknowledge(employee.Id, existing.Id, Ack(typed)));

        Assert.Equal(WriteUpAcknowledgment.Acknowledged, acknowledged.AcknowledgmentStatus);
        Assert.Equal(stored, acknowledged.AcknowledgmentSignedName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_name_does_not_acknowledge(string typed)
    {
        var existing = Create(employee);

        var result = As(employee).Acknowledge(employee.Id, existing.Id, Ack(typed));

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal(WriteUpAcknowledgment.Pending, db.WriteUps.Single().AcknowledgmentStatus);
        Assert.Single(db.WriteUpEvents);
    }

    [Theory]
    [InlineData("emp")]
    [InlineData("Tester")]
    [InlineData("emp Testerson")]
    [InlineData("emp2 Tester")]
    public void A_name_that_does_not_match_the_account_does_not_acknowledge_and_says_what_to_type(string typed)
    {
        var existing = Create(employee);

        var result = As(employee).Acknowledge(employee.Id, existing.Id, Ack(typed));

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("emp Tester", Assert.IsType<string>(bad.Value));
        Assert.Equal(WriteUpAcknowledgment.Pending, db.WriteUps.Single().AcknowledgmentStatus);
        Assert.Null(db.WriteUps.Single().AcknowledgmentSignedName);
        Assert.Single(db.WriteUpEvents);
    }

    [Fact]
    public void A_coworkers_name_does_not_acknowledge()
    {
        var existing = Create(employee);

        var result = As(employee).Acknowledge(employee.Id, existing.Id, Signed(otherEmployee));

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public void Acknowledging_again_needs_no_name_and_changes_nothing()
    {
        var existing = Create(employee);
        var first = Ok(As(employee).Acknowledge(employee.Id, existing.Id, Signed(employee)));

        // A retry, even one that arrives without a usable name, is a no-op
        // rather than an error or a second entry.
        var again = Ok(As(employee).Acknowledge(employee.Id, existing.Id, Ack("")));

        Assert.Equal(first.AcknowledgmentAt, again.AcknowledgmentAt);
        Assert.Equal("emp Tester", again.AcknowledgmentSignedName);
        Assert.Equal(2, db.WriteUpEvents.Count());
    }

    [Fact]
    public void An_edit_clears_the_signed_name_but_the_history_still_has_it()
    {
        var existing = Create(employee);
        As(employee).Acknowledge(employee.Id, existing.Id, Signed(employee));

        var edited = Edit(existing, description: "Materially different");

        Assert.Null(edited.AcknowledgmentSignedName);
        Assert.Equal("Signed: emp Tester", edited.History!.Single(e => e.Action == WriteUpEventAction.Acknowledged).Detail);
    }

    [Fact]
    public void A_declined_write_up_has_no_signed_name()
    {
        var existing = Create(employee);

        var declined = Ok(As(admin).DeclineAcknowledgment(employee.Id, existing.Id));

        Assert.Null(declined.AcknowledgmentSignedName);
    }

    [Fact]
    public void An_employee_who_declined_must_still_type_their_name_to_acknowledge_later()
    {
        var existing = Create(employee);
        As(admin).DeclineAcknowledgment(employee.Id, existing.Id);

        Assert.IsType<BadRequestObjectResult>(
            As(employee).Acknowledge(employee.Id, existing.Id, Ack("someone else")).Result);
        Assert.Equal(WriteUpAcknowledgment.Declined, db.WriteUps.Single().AcknowledgmentStatus);
    }

    // ---- drawn signature -------------------------------------------------

    private static FileContentResult Image(IActionResult result) => Assert.IsType<FileContentResult>(result);

    [Fact]
    public void Acknowledging_stores_the_drawn_signature_and_points_the_history_at_it()
    {
        var existing = Create(employee);
        var drawn = TestPng.Build(dataBytes: 321);

        var acknowledged = Ok(As(employee).Acknowledge(employee.Id, existing.Id, Ack("emp Tester", TestPng.DataUrl(drawn))));

        var stored = db.WriteUpSignatures.Single();
        Assert.Equal(drawn, stored.Png);
        Assert.Equal("emp Tester", stored.SignedName);
        Assert.Equal(existing.Id, stored.WriteUpId);
        Assert.Equal(stored.Id, acknowledged.AcknowledgmentSignatureId);

        var trail = Assert.Single(List(As(admin).GetAll(employee.Id))).History!;
        Assert.Equal(stored.Id, trail.Last().SignatureId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("http://example.com/sig.png")]
    [InlineData("data:image/png;base64,!!!")]
    public void Acknowledging_without_a_valid_signature_is_rejected_and_changes_nothing(string signature)
    {
        var existing = Create(employee);

        var result = As(employee).Acknowledge(employee.Id, existing.Id, Ack("emp Tester", signature));

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal(WriteUpAcknowledgment.Pending, db.WriteUps.Single().AcknowledgmentStatus);
        Assert.Empty(db.WriteUpSignatures);
        Assert.Single(db.WriteUpEvents);
    }

    [Fact]
    public void Acknowledging_rejects_an_image_that_is_not_a_png()
    {
        var existing = Create(employee);
        var notPng = "data:image/png;base64," + Convert.ToBase64String(new byte[500]);

        Assert.IsType<BadRequestObjectResult>(As(employee).Acknowledge(employee.Id, existing.Id, Ack("emp Tester", notPng)).Result);
        Assert.Empty(db.WriteUpSignatures);
    }

    [Fact]
    public void The_name_is_still_checked_before_the_signature_is_saved()
    {
        var existing = Create(employee);

        Assert.IsType<BadRequestObjectResult>(As(employee).Acknowledge(employee.Id, existing.Id, Ack("Someone Else")).Result);
        Assert.Empty(db.WriteUpSignatures);
    }

    [Fact]
    public void The_employee_and_their_admin_can_fetch_the_signature_image()
    {
        var existing = Create(employee);
        var drawn = TestPng.Build(dataBytes: 55);
        var id = Ok(As(employee).Acknowledge(employee.Id, existing.Id, Ack("emp Tester", TestPng.DataUrl(drawn)))).AcknowledgmentSignatureId!.Value;

        var mine = Image(As(employee).GetSignature(employee.Id, existing.Id, id));
        var theirs = Image(As(admin).GetSignature(employee.Id, existing.Id, id));

        Assert.Equal("image/png", mine.ContentType);
        Assert.Equal(drawn, mine.FileContents);
        Assert.Equal(drawn, theirs.FileContents);
    }

    [Fact]
    public void Nobody_else_can_fetch_a_signature_image()
    {
        var existing = Create(employee);
        var id = Ok(As(employee).Acknowledge(employee.Id, existing.Id, Signed(employee))).AcknowledgmentSignatureId!.Value;
        var lead = Seed("lead", AccountRole.Lead, location);
        var outsider = Seed("outsider", AccountRole.Admin, otherLocation);

        Assert.IsType<NotFoundResult>(As(otherEmployee).GetSignature(employee.Id, existing.Id, id));
        Assert.IsType<NotFoundResult>(As(lead).GetSignature(employee.Id, existing.Id, id));
        Assert.IsType<NotFoundResult>(As(outsider).GetSignature(employee.Id, existing.Id, id));
    }

    [Fact]
    public void A_signature_id_cannot_be_used_through_a_different_write_up_or_employee()
    {
        var mine = Create(employee);
        var theirs = Create(otherEmployee);
        var mySignature = Ok(As(employee).Acknowledge(employee.Id, mine.Id, Signed(employee))).AcknowledgmentSignatureId!.Value;
        var theirSignature = Ok(As(otherEmployee).Acknowledge(otherEmployee.Id, theirs.Id, Signed(otherEmployee))).AcknowledgmentSignatureId!.Value;

        // Right employee and write-up, but someone else's signature id.
        Assert.IsType<NotFoundResult>(As(admin).GetSignature(employee.Id, mine.Id, theirSignature));
        // Another employee's write-up path with my signature id.
        Assert.IsType<NotFoundResult>(As(admin).GetSignature(otherEmployee.Id, theirs.Id, mySignature));
        // An employee can't reach a coworker's by guessing ids either.
        Assert.IsType<NotFoundResult>(As(employee).GetSignature(otherEmployee.Id, theirs.Id, theirSignature));
        Assert.IsType<NotFoundResult>(As(admin).GetSignature(employee.Id, mine.Id, 9999));
    }

    [Fact]
    public void An_edit_drops_the_current_signature_but_the_original_stays_on_record()
    {
        var existing = Create(employee);
        var original = Ok(As(employee).Acknowledge(employee.Id, existing.Id, Ack("emp Tester", TestPng.DataUrl(TestPng.Build(dataBytes: 11)))))
            .AcknowledgmentSignatureId!.Value;

        var edited = Edit(existing, description: "Materially different");

        Assert.Null(edited.AcknowledgmentSignatureId);
        Assert.Single(db.WriteUpSignatures);

        // Still reachable through the history entry that pointed at it.
        var fromHistory = edited.History!.Single(e => e.Action == WriteUpEventAction.Acknowledged).SignatureId;
        Assert.Equal(original, fromHistory);
        Assert.Equal(TestPng.Build(dataBytes: 11), Image(As(admin).GetSignature(employee.Id, existing.Id, original)).FileContents);

        // Signing again adds a second one and becomes the current signature.
        var second = Ok(As(employee).Acknowledge(employee.Id, existing.Id, Ack("emp Tester", TestPng.DataUrl(TestPng.Build(dataBytes: 22)))));
        Assert.Equal(2, db.WriteUpSignatures.Count());
        Assert.NotEqual(original, second.AcknowledgmentSignatureId);
        Assert.Equal(TestPng.Build(dataBytes: 11), Image(As(admin).GetSignature(employee.Id, existing.Id, original)).FileContents);
    }

    [Fact]
    public void The_employee_sees_the_current_signature_id_but_not_the_history()
    {
        var existing = Create(employee);
        var id = Ok(As(employee).Acknowledge(employee.Id, existing.Id, Signed(employee))).AcknowledgmentSignatureId;

        var listed = Assert.Single(List(As(employee).GetAll(employee.Id)));

        Assert.Equal(id, listed.AcknowledgmentSignatureId);
        Assert.Null(listed.History);
    }

    [Fact]
    public void A_retry_after_acknowledging_adds_no_second_signature()
    {
        var existing = Create(employee);
        var first = Ok(As(employee).Acknowledge(employee.Id, existing.Id, Signed(employee)));

        var again = Ok(As(employee).Acknowledge(employee.Id, existing.Id, Ack("", "")));

        Assert.Equal(first.AcknowledgmentSignatureId, again.AcknowledgmentSignatureId);
        Assert.Single(db.WriteUpSignatures);
    }

    [Fact]
    public void A_declined_write_up_has_no_signature_and_signing_later_still_needs_one()
    {
        var existing = Create(employee);

        var declined = Ok(As(admin).DeclineAcknowledgment(employee.Id, existing.Id));
        Assert.Null(declined.AcknowledgmentSignatureId);

        Assert.IsType<BadRequestObjectResult>(As(employee).Acknowledge(employee.Id, existing.Id, Ack("emp Tester", "")).Result);
        Assert.Equal(WriteUpAcknowledgment.Declined, db.WriteUps.Single().AcknowledgmentStatus);
        Assert.Equal(WriteUpAcknowledgment.Acknowledged, Ok(As(employee).Acknowledge(employee.Id, existing.Id, Signed(employee))).AcknowledgmentStatus);
    }

    [Fact]
    public void A_write_up_acknowledged_before_signatures_existed_simply_has_none()
    {
        var existing = Create(employee);
        var row = db.WriteUps.Single();
        row.AcknowledgmentStatus = WriteUpAcknowledgment.Acknowledged;
        row.AcknowledgmentAt = DateTime.UtcNow;
        row.AcknowledgmentSignedName = "emp Tester";
        db.WriteUpEvents.Add(new WriteUpEvent { WriteUpId = existing.Id, Action = WriteUpEventAction.Acknowledged, ByAccountId = employee.Id, Detail = "Signed: emp Tester" });
        db.SaveChanges();

        var listed = Assert.Single(List(As(employee).GetAll(employee.Id)));

        Assert.Equal(WriteUpAcknowledgment.Acknowledged, listed.AcknowledgmentStatus);
        Assert.Null(listed.AcknowledgmentSignatureId);
    }

    [Fact]
    public void Acknowledging_again_changes_nothing()
    {
        var existing = Create(employee);
        var first = Ok(As(employee).Acknowledge(employee.Id, existing.Id, Signed(employee)));

        var second = Ok(As(employee).Acknowledge(employee.Id, existing.Id, Signed(employee)));

        Assert.Equal(first.AcknowledgmentAt, second.AcknowledgmentAt);
        Assert.Equal(2, db.WriteUpEvents.Count());
    }

    [Fact]
    public void Nobody_but_the_employee_can_acknowledge_for_them()
    {
        var existing = Create(employee);

        Assert.IsType<NotFoundResult>(As(admin).Acknowledge(employee.Id, existing.Id, Signed(employee)).Result);
        Assert.IsType<NotFoundResult>(As(otherEmployee).Acknowledge(employee.Id, existing.Id, Signed(employee)).Result);
        Assert.Equal(WriteUpAcknowledgment.Pending, db.WriteUps.Single().AcknowledgmentStatus);
    }

    [Fact]
    public void An_admin_can_record_that_the_employee_declined_to_acknowledge()
    {
        var existing = Create(employee);

        var declined = Ok(As(admin).DeclineAcknowledgment(employee.Id, existing.Id));

        Assert.Equal(WriteUpAcknowledgment.Declined, declined.AcknowledgmentStatus);
        Assert.NotNull(declined.AcknowledgmentAt);
        var entry = declined.History!.Last();
        Assert.Equal(WriteUpEventAction.AcknowledgmentDeclined, entry.Action);
        Assert.Equal(admin.Id, entry.ByAccountId);
    }

    [Fact]
    public void Recording_a_decline_twice_changes_nothing()
    {
        var existing = Create(employee);
        As(admin).DeclineAcknowledgment(employee.Id, existing.Id);

        Ok(As(admin).DeclineAcknowledgment(employee.Id, existing.Id));

        Assert.Equal(2, db.WriteUpEvents.Count());
    }

    [Fact]
    public void A_decline_cannot_be_recorded_over_an_acknowledgment()
    {
        var existing = Create(employee);
        As(employee).Acknowledge(employee.Id, existing.Id, Signed(employee));

        Assert.IsType<ConflictObjectResult>(As(admin).DeclineAcknowledgment(employee.Id, existing.Id).Result);
        Assert.Equal(WriteUpAcknowledgment.Acknowledged, db.WriteUps.Single().AcknowledgmentStatus);
    }

    [Fact]
    public void An_employee_who_declined_can_still_acknowledge_later()
    {
        var existing = Create(employee);
        As(admin).DeclineAcknowledgment(employee.Id, existing.Id);

        Assert.Equal(WriteUpAcknowledgment.Acknowledged, Ok(As(employee).Acknowledge(employee.Id, existing.Id, Signed(employee))).AcknowledgmentStatus);
    }

    [Fact]
    public void History_lists_events_oldest_first()
    {
        var existing = Create(employee);
        Edit(existing, description: "Reworded");
        As(employee).Acknowledge(employee.Id, existing.Id, Signed(employee));

        var trail = Assert.Single(List(As(admin).GetAll(employee.Id))).History!;

        Assert.Equal(
            [WriteUpEventAction.Created, WriteUpEventAction.Edited, WriteUpEventAction.Acknowledged],
            trail.Select(e => e.Action));
    }

    // ---- write-ups are permanent -----------------------------------------

    private AccountsController AccountsAsSa() => new(
        db, null!, new SsnProtector(new EphemeralDataProtectionProvider()),
        new PinProtector(new EphemeralDataProtectionProvider()), new ConfigurationBuilder().Build())
    {
        ControllerContext = ContextFor(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, nameof(AccountRole.Sa))], "test"))),
    };

    [Fact]
    public void An_account_with_write_ups_cannot_be_deleted()
    {
        Create(employee);

        Assert.IsType<ConflictObjectResult>(AccountsAsSa().Delete(employee.Id));
        Assert.NotNull(db.Accounts.Find(employee.Id));
        Assert.Single(db.WriteUps);
    }

    [Fact]
    public void An_account_that_recorded_a_write_up_cannot_be_deleted_either()
    {
        Create(employee);

        Assert.IsType<ConflictObjectResult>(AccountsAsSa().Delete(admin.Id));
        Assert.NotNull(db.Accounts.Find(admin.Id));
    }

    [Fact]
    public void An_account_with_no_write_ups_can_still_be_deleted()
    {
        Create(employee);

        Assert.IsType<NoContentResult>(AccountsAsSa().Delete(otherEmployee.Id));
        Assert.Null(db.Accounts.Find(otherEmployee.Id));
    }

    [Fact]
    public void The_database_itself_refuses_to_delete_an_account_that_has_write_ups()
    {
        Create(employee);

        // A fresh context, so nothing is tracked and EF's own client-side
        // check can't fire first — this has to be SQLite's foreign key.
        using var fresh = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        fresh.Accounts.Remove(fresh.Accounts.Single(a => a.Id == employee.Id));

        Assert.Throws<DbUpdateException>(() => fresh.SaveChanges());
    }
}
