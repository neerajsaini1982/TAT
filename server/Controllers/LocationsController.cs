using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Server.Data;
using Server.Dtos;
using Server.Models;
using Server.Services;

namespace Server.Controllers;

[ApiController]
[Route("api/locations")]
[Authorize(Policy = "SaOnly")]
public partial class LocationsController(AppDbContext db) : ControllerBase
{
    [GeneratedRegex("^[a-z0-9]{3,10}$")]
    private static partial Regex LocationCodePattern();

    [HttpGet]
    public ActionResult<IEnumerable<LocationDto>> GetAll() =>
        Ok(db.Locations.OrderBy(l => l.Name).Select(ToDto));

    [HttpGet("{id:int}")]
    public ActionResult<LocationDto> Get(int id)
    {
        var location = db.Locations.Find(id);
        return location is null ? NotFound() : Ok(ToDto(location));
    }

    [HttpPost]
    public ActionResult<LocationDto> Create(CreateLocationRequest request)
    {
        var code = request.LocationCode.Trim().ToLowerInvariant();
        if (!LocationCodePattern().IsMatch(code))
        {
            return BadRequest("Location code must be 3-10 lowercase letters/digits.");
        }

        if (db.Locations.Any(l => l.LocationCode == code))
        {
            return Conflict($"Location code '{code}' is already in use.");
        }

        var location = new Location
        {
            Name = request.Name,
            Address = request.Address,
            LocationCode = code,
            Phone = request.Phone,
            Email = request.Email,
        };

        db.Locations.Add(location);
        db.SaveChanges();

        return CreatedAtAction(nameof(Get), new { id = location.Id }, ToDto(location));
    }

    // Creates the "demo" location for showing the product to prospects, or
    // wipes and rebuilds it if it already exists — see DemoLocationSeeder.
    // Only ever touches the location with that exact code.
    [HttpPost("demo")]
    public ActionResult<DemoLocationResult> ResetDemo(ResetDemoLocationRequest request, [FromServices] DemoLocationSeeder seeder)
    {
        if (string.IsNullOrWhiteSpace(request.AdminPassword) || request.AdminPassword.Length < 8)
        {
            return BadRequest("The demo password must be at least 8 characters.");
        }

        if (string.IsNullOrWhiteSpace(request.KioskPasscode) || request.KioskPasscode.Length < 4)
        {
            return BadRequest("The kiosk passcode must be at least 4 characters.");
        }

        return Ok(seeder.Reset(request.AdminPassword, request.KioskPasscode));
    }

    [HttpPut("{id:int}")]
    public ActionResult<LocationDto> Update(int id, UpdateLocationRequest request)
    {
        var location = db.Locations.Find(id);
        if (location is null)
        {
            return NotFound();
        }

        location.Name = request.Name;
        location.Address = request.Address;
        location.Phone = request.Phone;
        location.Email = request.Email;
        location.IsActive = request.IsActive;
        db.SaveChanges();

        return Ok(ToDto(location));
    }

    [HttpDelete("{id:int}")]
    public IActionResult Delete(int id)
    {
        var location = db.Locations.Find(id);
        if (location is null)
        {
            return NotFound();
        }

        if (db.Accounts.Any(a => a.LocationId == id))
        {
            return Conflict("Cannot delete a location that still has accounts linked to it.");
        }

        db.Locations.Remove(location);
        db.SaveChanges();
        return NoContent();
    }

    private static LocationDto ToDto(Location l) =>
        new(l.Id, l.Name, l.Address, l.LocationCode, l.Phone, l.Email, l.IsActive);
}
