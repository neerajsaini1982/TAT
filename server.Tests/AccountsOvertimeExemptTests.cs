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

// Account.IsOvertimeExempt through the create/update endpoints.
public sealed class AccountsOvertimeExemptTests : IDisposable
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly AppDbContext db;
    private readonly Location location;

    public AccountsOvertimeExemptTests()
    {
        connection.Open();
        db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        db.Database.EnsureCreated();
        location = new Location { Name = "Test", LocationCode = "t1" };
        db.Locations.Add(location);
        db.SaveChanges();
    }

    public void Dispose()
    {
        db.Dispose();
        connection.Dispose();
    }

    // The email sender is only used by the send-credentials endpoint.
    private AccountsController CreateController() => new(
        db, null!, new SsnProtector(new EphemeralDataProtectionProvider()), new ConfigurationBuilder().Build())
    {
        ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, nameof(AccountRole.Sa))], "test")),
            },
        },
    };

    private AccountDto Create(bool? exempt = null)
    {
        var request = new CreateAccountRequest(
            Username: null, Password: null, FirstName: "Pat", LastName: "Lee", Email: "", Phone: "",
            AccountRole.Employee, location.Id, HourlyRate: null, Ssn: null, DateOfBirth: null, HireDate: null, EmploymentType: null);
        if (exempt is { } value)
        {
            request = request with { IsOvertimeExempt = value };
        }

        var result = CreateController().Create(request);
        return Assert.IsType<AccountDto>(Assert.IsType<CreatedAtActionResult>(result.Result).Value);
    }

    private AccountDto Update(int id, bool exempt)
    {
        var request = new UpdateAccountRequest(
            "Pat", "Lee", "", "", IsActive: true, IsOnShiftSchedule: true, CanSeeAllSchedules: false, AccountRole.Employee,
            Username: null, Password: null, HourlyRate: null, Ssn: null, DateOfBirth: null, HireDate: null, EmploymentType: null,
            IsOvertimeExempt: exempt);

        var result = CreateController().Update(id, request);
        return Assert.IsType<AccountDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
    }

    [Fact]
    public void A_new_account_is_not_exempt_unless_asked()
    {
        var account = Create();

        Assert.False(account.IsOvertimeExempt);
        Assert.False(db.Accounts.Single(a => a.Id == account.Id).IsOvertimeExempt);
    }

    [Fact]
    public void An_account_can_be_created_exempt()
    {
        var account = Create(exempt: true);

        Assert.True(account.IsOvertimeExempt);
        Assert.True(db.Accounts.Single(a => a.Id == account.Id).IsOvertimeExempt);
    }

    [Fact]
    public void The_flag_can_be_turned_on_and_off_again_on_an_existing_account()
    {
        var id = Create().Id;

        Assert.True(Update(id, exempt: true).IsOvertimeExempt);
        Assert.True(db.Accounts.Single(a => a.Id == id).IsOvertimeExempt);

        Assert.False(Update(id, exempt: false).IsOvertimeExempt);
        Assert.False(db.Accounts.Single(a => a.Id == id).IsOvertimeExempt);
    }

    [Fact]
    public void Full_time_employment_does_not_imply_exempt()
    {
        var request = new CreateAccountRequest(
            null, null, "Pat", "Lee", "", "", AccountRole.Employee, location.Id,
            HourlyRate: 22m, Ssn: null, DateOfBirth: null, HireDate: null, EmploymentType: EmploymentType.FullTime);

        var result = CreateController().Create(request);

        var account = Assert.IsType<AccountDto>(Assert.IsType<CreatedAtActionResult>(result.Result).Value);
        Assert.Equal("FullTime", account.EmploymentType);
        Assert.False(account.IsOvertimeExempt);
    }
}
