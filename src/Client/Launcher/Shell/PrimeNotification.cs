using System;

namespace MphRead.Mods.Launcher.Gui;

public enum PrimeNotificationKind
{
    Success,
    Info,
    Warning,
    Error
}
/// <summary>A short shell-level status message. Routine status is non-modal.</summary>
public sealed record PrimeNotification(PrimeNotificationKind Kind, string Message,
    DateTimeOffset CreatedAt, TimeSpan? Duration = null)
{
    public PrimeNotification(PrimeNotificationKind kind, string message)
        : this(kind, Validate(message), DateTimeOffset.UtcNow) { }

    private static string Validate(string message)
    {
        if (message is not { Length: > 0 and <= 512 })
            throw new ArgumentException("A notification must contain 1 to 512 characters.", nameof(message));
        return message;
    }
}
