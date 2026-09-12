using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;
using MphRead.Identity;

namespace MphRead.Backend.Identity;

public interface IConfirmationEmail
{
    bool IsConfigured { get; }
    Task<bool> TrySendAsync(string email, PlayerId playerId, string code, CancellationToken cancellationToken);
}

// Real delivery only; absent SMTP configuration is an explicit unavailable result.
public sealed class ConfirmationEmail(IOptions<EmailOptions> options, ILogger<ConfirmationEmail> logger) : IConfirmationEmail
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(options.Value.Host)
        && options.Value.Host.Length <= 253 && !options.Value.Host.Any(char.IsControl)
        && options.Value.Port is >= 1 and <= 65535
        && ValidAddress(options.Value.Sender)
        && options.Value.TimeoutSeconds is >= 1 and <= 60
        && (string.IsNullOrEmpty(options.Value.Username) == string.IsNullOrEmpty(options.Value.Password));

    public async Task<bool> TrySendAsync(string email, PlayerId playerId, string code, CancellationToken cancellationToken)
    {
        if (!IsConfigured) { throw new InvalidOperationException("Confirmation email is not configured."); }
        EmailOptions settings = options.Value;
        using var smtp = new SmtpClient(settings.Host!, settings.Port)
        {
            EnableSsl = true,
            Timeout = checked(settings.TimeoutSeconds * 1000)
        };
        if (!string.IsNullOrEmpty(settings.Username))
        {
            smtp.Credentials = new NetworkCredential(settings.Username, settings.Password);
        }
        using var message = new MailMessage(settings.Sender!, email)
        {
            Subject = "Confirm your Project Prime account",
            Body = $"Enter this confirmation code in Project Prime:\n\n{code}\n\nIf you did not register, ignore this message."
        };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));
        try
        {
            await smtp.SendMailAsync(message, timeout.Token);
            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Confirmation email provider timed out.");
            return false;
        }
        catch (SmtpException exception)
        {
            // SMTP exception messages can contain recipient data. Record only the failure type.
            logger.LogWarning("Confirmation email provider was unavailable ({FailureType}).", exception.GetType().Name);
            return false;
        }
    }

    private static bool ValidAddress(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 254) return false;
        try { return new MailAddress(value).Address == value; }
        catch (FormatException) { return false; }
    }
}
