using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Server.Controllers;
using Server.Data;
using Server.Dtos;
using Server.Models;
using Server.Security;

namespace Server.Tests;

// Who can see and change write-ups (issue #88): an employee sees only their
// own; only an Admin/Sa of the employee's location can create, edit, delete,
// or see anyone else's.
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

    private WriteUpsController As(Account caller, string? locationCode = null)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, caller.Id.ToString()),
            new(ClaimTypes.Role, caller.Role.ToString()),
        };
        var code = locationCode ?? (caller.LocationId is null ? null : db.Locations.Find(caller.LocationId)!.LocationCode);
        if (code is not null)
        {
            claims.Add(new Claim(TokenService.LocationCodeClaimType, code));
        }

        return new WriteUpsController(db)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) },
            },
        };
    }

    private WriteUpDto Create(Account target, string description = "Late twice this week", WriteUpSeverity? severity = null, DateOnly? date = null)
    {
        var result = As(admin).Create(target.Id, new CreateWriteUpRequest(date ?? Today, description, severity));
        return Assert.IsType<WriteUpDto>(Assert.IsType<CreatedAtActionResult>(result.Result).Value);
    }

    private static List<WriteUpDto> List(ActionResult<IEnumerable<WriteUpDto>> result) =>
        Assert.IsAssignableFrom<IEnumerable<WriteUpDto>>(Assert.IsType<OkObjectResult>(result.Result).Value).ToList();

    [Fact]
    public void Severity_defaults_to_Normal_when_omitted()
    {
        var created = Create(employee);

        Assert.Equal(WriteUpSeverity.Normal, created.Severity);
        Assert.Equal(WriteUpSeverity.Normal, db.WriteUps.Single().Severity);
    }

    [Fact]
    public void Create_keeps_the_chosen_severity_date_and_author()
    {
        var created = Create(employee, "  No call, no show  ", WriteUpSeverity.Critical, new DateOnly(2026, 9, 1));

        Assert.Equal(WriteUpSeverity.Critical, created.Severity);
        Assert.Equal(new DateOnly(2026, 9, 1), created.Date);
        Assert.Equal("No call, no show", created.Description);
        Assert.Equal(admin.Id, created.CreatedByAccountId);
        Assert.Equal("admin Tester", created.CreatedByName);
    }

    [Fact]
    public void Severity_is_stored_by_name()
    {
        Create(employee, severity: WriteUpSeverity.High);

        var stored = db.Database.SqlQueryRaw<string>("SELECT Severity AS Value FROM WriteUps").Single();
        Assert.Equal("High", stored);
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
        var result = As(admin).Create(employee.Id, new CreateWriteUpRequest(default, "Late"));

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public void Create_rejects_an_over_long_description()
    {
        var tooLong = new string('x', WriteUpsController.MaxDescriptionLength + 1);

        var result = As(admin).Create(employee.Id, new CreateWriteUpRequest(Today, tooLong));

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public void Create_rejects_an_undefined_severity()
    {
        var result = As(admin).Create(employee.Id, new CreateWriteUpRequest(Today, "Late", (WriteUpSeverity)99));

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public void An_employee_sees_their_own_write_ups_newest_date_first()
    {
        Create(employee, "older", date: new DateOnly(2026, 8, 1));
        Create(employee, "newer", date: new DateOnly(2026, 9, 10));
        Create(otherEmployee, "someone else's");

        var mine = List(As(employee).GetAll(employee.Id));

        Assert.Equal(["newer", "older"], mine.Select(w => w.Description));
    }

    [Fact]
    public void An_employee_cannot_read_a_coworkers_write_ups()
    {
        Create(otherEmployee);

        Assert.IsType<NotFoundResult>(As(employee).GetAll(otherEmployee.Id).Result);
    }

    [Fact]
    public void An_admin_can_read_an_employees_write_ups()
    {
        Create(employee);

        Assert.Single(List(As(admin).GetAll(employee.Id)));
    }

    [Fact]
    public void A_lead_cannot_read_someone_elses_write_ups()
    {
        var lead = Seed("lead", AccountRole.Lead, location);
        Create(employee);

        Assert.IsType<NotFoundResult>(As(lead).GetAll(employee.Id).Result);
    }

    [Fact]
    public void An_admin_at_another_location_cannot_read_or_write()
    {
        var outsider = Seed("outsider", AccountRole.Admin, otherLocation);
        Create(employee);

        Assert.IsType<NotFoundResult>(As(outsider).GetAll(employee.Id).Result);
        Assert.IsType<NotFoundResult>(As(outsider).Create(employee.Id, new CreateWriteUpRequest(Today, "x")).Result);
    }

    [Fact]
    public void Sa_can_manage_write_ups_at_any_location()
    {
        var sa = Seed("sa", AccountRole.Sa, null);

        var result = As(sa).Create(employee.Id, new CreateWriteUpRequest(Today, "Late"));

        Assert.IsType<CreatedAtActionResult>(result.Result);
    }

    [Fact]
    public void An_employee_cannot_create_update_or_delete_even_for_themselves()
    {
        var existing = Create(employee);

        Assert.IsType<NotFoundResult>(As(employee).Create(employee.Id, new CreateWriteUpRequest(Today, "x")).Result);
        Assert.IsType<NotFoundResult>(
            As(employee).Update(employee.Id, existing.Id, new UpdateWriteUpRequest(Today, "x", WriteUpSeverity.Low)).Result);
        Assert.IsType<NotFoundResult>(As(employee).Delete(employee.Id, existing.Id));
        Assert.Equal("Late twice this week", db.WriteUps.Single().Description);
    }

    [Fact]
    public void Update_changes_the_fields()
    {
        var existing = Create(employee);

        var result = As(admin).Update(
            employee.Id, existing.Id, new UpdateWriteUpRequest(new DateOnly(2026, 9, 2), "Reworded", WriteUpSeverity.High));

        var updated = Assert.IsType<WriteUpDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal("Reworded", updated.Description);
        Assert.Equal(WriteUpSeverity.High, updated.Severity);
        Assert.Equal(new DateOnly(2026, 9, 2), updated.Date);
    }

    [Fact]
    public void Update_only_reaches_a_write_up_that_belongs_to_that_employee()
    {
        var theirs = Create(otherEmployee);

        var result = As(admin).Update(employee.Id, theirs.Id, new UpdateWriteUpRequest(Today, "x", WriteUpSeverity.Low));

        Assert.IsType<NotFoundResult>(result.Result);
        Assert.Equal("Late twice this week", db.WriteUps.Single().Description);
    }

    [Fact]
    public void Delete_removes_the_write_up()
    {
        var existing = Create(employee);

        Assert.IsType<NoContentResult>(As(admin).Delete(employee.Id, existing.Id));
        Assert.Empty(db.WriteUps);
    }

    [Fact]
    public void Deleting_an_employee_account_removes_their_write_ups()
    {
        Create(employee);

        db.Accounts.Remove(employee);
        db.SaveChanges();

        Assert.Empty(db.WriteUps);
    }
}
