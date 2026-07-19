using System.Net;
using System.Net.Mail;
using Server.Models;

namespace Server.Services;

public interface IEmailSender
{
    Task SendAsync(LocationSettings settings, string toAddress, string subject, string bodyHtml);
}

// Thin wrapper around System.Net.Mail so callers don't need to know how to
// turn a LocationSettings row into an SmtpClient. First real caller is
// AccountsController.SendCredentials.
public class SmtpEmailSender : IEmailSender
{
    public async Task SendAsync(LocationSettings settings, string toAddress, string subject, string bodyHtml)
    {
        if (string.IsNullOrWhiteSpace(settings.SmtpHost))
        {
            throw new InvalidOperationException("SMTP is not configured for this location. Set it up under Settings first.");
        }

        using var client = new SmtpClient(settings.SmtpHost, settings.SmtpPort ?? 587)
        {
            EnableSsl = settings.SmtpUseSsl,
        };

        if (!string.IsNullOrEmpty(settings.SmtpUsername))
        {
            client.Credentials = new NetworkCredential(settings.SmtpUsername, settings.SmtpPassword);
        }

        var fromAddress = string.IsNullOrWhiteSpace(settings.SmtpFromAddress)
            ? settings.SmtpUsername
            : settings.SmtpFromAddress;
        if (string.IsNullOrWhiteSpace(fromAddress))
        {
            throw new InvalidOperationException("SMTP has no from address configured for this location.");
        }

        using var message = new MailMessage
        {
            From = new MailAddress(fromAddress, settings.SmtpFromName),
            Subject = subject,
            Body = bodyHtml,
            IsBodyHtml = true,
        };
        message.To.Add(toAddress);

        await client.SendMailAsync(message);
    }
}
