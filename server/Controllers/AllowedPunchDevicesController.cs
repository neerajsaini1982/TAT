using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Server.Data;
using Server.Dtos;
using Server.Models;
using Server.Security;

namespace Server.Controllers;

// Manages the per-location IP allowlist used when
// LocationSettings.ClockInAnywhere is off — see TimeEntriesController for
// where these are enforced.
[ApiController]
[Route("api/allowed-punch-devices")]
[Authorize(Policy = "AdminOrAbove")]
public class AllowedPunchDevicesController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public ActionResult<IEnumerable<AllowedPunchDeviceDto>> GetAll([FromQuery] string? locationCode)
    {
        var location = ResolveLocation(locationCode);
        if (location is null)
        {
            return BadRequest("A valid locationCode is required.");
        }

        var devices = db.AllowedPunchDevices
            .Where(d => d.LocationId == location.Id)
            .OrderBy(d => d.CreatedAt)
            .Select(ToDto)
            .ToList();

        return Ok(devices);
    }

    [HttpPost]
    public ActionResult<AllowedPunchDeviceDto> Create(CreateAllowedPunchDeviceRequest request, [FromQuery] string? locationCode)
    {
        var location = ResolveLocation(locationCode);
        if (location is null)
        {
            return BadRequest("A valid locationCode is required.");
        }

        if (!IPAddress.TryParse(request.IpAddress, out _))
        {
            return BadRequest("Enter a valid IP address.");
        }

        if (string.IsNullOrWhiteSpace(request.Label))
        {
            return BadRequest("Enter a label for this device.");
        }

        if (db.AllowedPunchDevices.Any(d => d.LocationId == location.Id && d.IpAddress == request.IpAddress))
        {
            return Conflict("This IP address is already on the allowlist.");
        }

        var device = new AllowedPunchDevice
        {
            LocationId = location.Id,
            IpAddress = request.IpAddress,
            Label = request.Label,
        };
        db.AllowedPunchDevices.Add(device);
        db.SaveChanges();

        return Ok(ToDto(device));
    }

    [HttpDelete("{id:int}")]
    public IActionResult Delete(int id, [FromQuery] string? locationCode)
    {
        var location = ResolveLocation(locationCode);
        if (location is null)
        {
            return BadRequest("A valid locationCode is required.");
        }

        var device = db.AllowedPunchDevices.SingleOrDefault(d => d.Id == id && d.LocationId == location.Id);
        if (device is null)
        {
            return NotFound();
        }

        db.AllowedPunchDevices.Remove(device);
        db.SaveChanges();

        return NoContent();
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

    private string? CallerLocationCode() =>
        User.FindFirst(TokenService.LocationCodeClaimType)?.Value;

    private static AllowedPunchDeviceDto ToDto(AllowedPunchDevice d) =>
        new(d.Id, d.IpAddress, d.Label, d.CreatedAt);
}
