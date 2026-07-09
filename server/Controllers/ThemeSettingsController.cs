using Microsoft.AspNetCore.Mvc;
using Server.Models;

namespace Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ThemeSettingsController : ControllerBase
{
    // In-memory placeholder until this is backed by Firestore and admin auth.
    private static ThemeSettings _current = new();

    [HttpGet]
    public ActionResult<ThemeSettings> Get() => Ok(_current);

    [HttpPut]
    public ActionResult<ThemeSettings> Update([FromBody] ThemeSettings settings)
    {
        _current = settings;
        return Ok(_current);
    }
}
