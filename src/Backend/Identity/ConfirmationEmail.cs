using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;
using MphRead.Identity;

namespace MphRead.Backend.Identity;

public interface IConfirmationEmail
{
    bool IsConfigured { get; }
    Task SendAsync(string email, PlayerId playerId, string code, CancellationToken cancellationToken);
}

// Real delivery only; absent SMTP configuration is an explicit unavailable result.
public sealed class ConfirmationEmail(IOptions<EmailOptions> options) : IConfirmationEmail
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(options.Value.Host)
        && !string.IsNullOrWhiteSpace(options.Value.Sender);

    public async Task SendAsync(string email, PlayerId playerId, string code, CancellationToken cancellationToken)
    {
        if (!IsConfigured) { throw new InvalidOperationException("Confirmation email is not configured."); }
        EmailOptions settings = options.Value;
        using var smtp = new SmtpClient(settings.Host!, settings.Port) { EnableSsl = true };
        if (!string.IsNullOrEmpty(settings.Username))
        {
            smtp.Credentials = new NetworkCredential(settings.Username, settings.Password);
        }
        using var message = new MailMessage(settings.Sender!, email)
        {
            Subject = "Confirm your Prime Hunters account",
            Body = $"Enter this confirmation code in Prime Hunters.\n\nPlayer: {playerId}\nCode: {code}\n\nIf you did not register, ignore this message."
        };
        await smtp.SendMailAsync(message, cancellationToken);
    }
}
