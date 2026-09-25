using Server.Models;
using Server.Services;

namespace Server.Tests;

// Captures what a controller would have sent instead of talking to SMTP.
public sealed class RecordingEmailSender : IEmailSender
{
    public List<(string To, string Subject, string BodyHtml)> Sent { get; } = [];

    public Task SendAsync(LocationSettings settings, string toAddress, string subject, string bodyHtml)
    {
        Sent.Add((toAddress, subject, bodyHtml));
        return Task.CompletedTask;
    }
}
