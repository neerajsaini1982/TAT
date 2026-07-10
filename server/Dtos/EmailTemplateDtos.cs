namespace Server.Dtos;

public record EmailTemplateDto(string Key, string DisplayName, string Subject, string BodyHtml, DateTime UpdatedAt);

public record UpdateEmailTemplateRequest(string Subject, string BodyHtml);
