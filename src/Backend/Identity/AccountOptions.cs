namespace MphRead.Backend.Identity;

public sealed class AccountOptions
{
    // Disabling this is for private testing only and never confers official eligibility.
    public bool RequireConfirmedEmail { get; set; } = true;
    public string? DataProtectionKeyPath { get; set; }
    public int ConfirmationTokenLifetimeMinutes { get; set; } = 60;
    public int ConfirmationResendWindowSeconds { get; set; } = 900;
    public int ConfirmationResendsPerAccount { get; set; } = 3;
    public int ConfirmationResendsPerIp { get; set; } = 10;
    public int ConfirmationResendTrackedEntries { get; set; } = 10_000;
}

public sealed class EmailOptions
{
    public string? Host { get; set; }
    public int Port { get; set; } = 587;
    public string? Sender { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
    public int TimeoutSeconds { get; set; } = 10;
}
