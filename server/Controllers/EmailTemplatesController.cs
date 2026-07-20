using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Server.Data;
using Server.Dtos;
using Server.Models;
using Server.Security;

namespace Server.Controllers;

// Manages the fixed set of email templates (see EmailTemplateKeys) used
// elsewhere in the app — content only, no sending here. LoginCredentials is
// the first one actually sent (AccountsController.SendCredentials), using
// SMTP settings from LocationSettingsController.
[ApiController]
[Route("api/email-templates")]
[Authorize(Policy = "AdminOrAbove")]
public class EmailTemplatesController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public ActionResult<IEnumerable<EmailTemplateDto>> GetAll([FromQuery] string? locationCode)
    {
        var location = ResolveLocation(locationCode);
        if (location is null)
        {
            return BadRequest("A valid locationCode is required.");
        }

        var existing = db.EmailTemplates
            .Where(t => t.LocationId == location.Id)
            .ToDictionary(t => t.Key);

        var result = EmailTemplateKeys.All.Select(key =>
            ToDto(existing.TryGetValue(key, out var t) ? t : EmailTemplateCatalog.Default(key)));

        return Ok(result);
    }

    [HttpPut("{key}")]
    public ActionResult<EmailTemplateDto> Update(string key, UpdateEmailTemplateRequest request, [FromQuery] string? locationCode)
    {
        if (!EmailTemplateKeys.All.Contains(key))
        {
            return NotFound();
        }

        var location = ResolveLocation(locationCode);
        if (location is null)
        {
            return BadRequest("A valid locationCode is required.");
        }

        var template = db.EmailTemplates.SingleOrDefault(t => t.LocationId == location.Id && t.Key == key);
        if (template is null)
        {
            template = new EmailTemplate { LocationId = location.Id, Key = key };
            db.EmailTemplates.Add(template);
        }

        template.Subject = request.Subject;
        template.BodyHtml = request.BodyHtml;
        template.UpdatedAt = DateTime.UtcNow;
        db.SaveChanges();

        return Ok(ToDto(template));
    }

    private Location? ResolveLocation(string? locationCode)
    {
        if (User.IsInRole(nameof(AccountRole.Sa)))
        {
            return string.IsNullOrWhiteSpace(locationCode)
                ? null
                : db.Locations.SingleOrDefault(l => l.LocationCode == locationCode);
        }

        var callerLocationCode = User.FindFirst(TokenService.LocationCodeClaimType)?.Value;
        return db.Locations.SingleOrDefault(l => l.LocationCode == callerLocationCode);
    }

    private static EmailTemplateDto ToDto(EmailTemplate t) =>
        new(t.Key, EmailTemplateCatalog.DisplayName(t.Key), t.Subject, t.BodyHtml, t.UpdatedAt);
}
