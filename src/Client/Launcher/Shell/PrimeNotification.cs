using System;

namespace MphRead.Mods.Launcher.Gui;

public enum PrimeNotificationKind
{
    Success,
    Info,
    Warning,
    Error
}

public enum PrimeNotificationScope
{
    Transient,
    Route,
    Global
}

/// <summary>A short, keyed shell-level status message. Routine status is non-modal.</summary>
public sealed record PrimeNotification
{
    public string Key { get; }
    public PrimeNotificationKind Kind { get; }
    public string Message { get; }
    public PrimeNotificationScope Scope { get; }
    public DateTimeOffset CreatedAt { get; }
    public TimeSpan? Duration { get; }
    public PrimeRoute? Route { get; }
    public DateTimeOffset? ExpiresAt => Duration is { } duration
        ? CreatedAt + duration : null;

    public PrimeNotification(string key, PrimeNotificationKind kind, string message,
        PrimeNotificationScope scope, TimeSpan? duration = null, PrimeRoute? route = null,
        DateTimeOffset? createdAt = null)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (!Enum.IsDefined(scope)) throw new ArgumentOutOfRangeException(nameof(scope));
        if (scope == PrimeNotificationScope.Route && route is null)
            throw new ArgumentException("A route notification requires its owning route.", nameof(route));
        if (scope != PrimeNotificationScope.Route && route is not null)
            throw new ArgumentException("Only route notifications can own a route.", nameof(route));
        Key = ValidateKey(key);
        Kind = kind;
        Message = Validate(message);
        Scope = scope;
        CreatedAt = createdAt ?? DateTimeOffset.UtcNow;
        Duration = ValidateDuration(duration);
        Route = route;
    }

    private static string ValidateKey(string key)
    {
        string value = key?.Trim() ?? "";
        if (value is not { Length: > 0 and <= 96 })
            throw new ArgumentException("A notification key must contain 1 to 96 characters.", nameof(key));
        return value;
    }

    private static string Validate(string message)
    {
        if (message is not { Length: > 0 and <= 512 })
            throw new ArgumentException("A notification must contain 1 to 512 characters.", nameof(message));
        return message;
    }

    private static TimeSpan? ValidateDuration(TimeSpan? duration)
    {
        if (duration is { } value && (value <= TimeSpan.Zero || value > TimeSpan.FromDays(1)))
            throw new ArgumentOutOfRangeException(nameof(duration));
        return duration;
    }
}
