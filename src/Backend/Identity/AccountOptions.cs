namespace MphRead.Backend.Identity;

public sealed class AccountOptions
{
    // Disabling this is for private testing only and never confers official eligibility.
    public bool RequireConfirmedEmail { get; set; } = true;
    public string? DataProtectionKeyPath { get; set; }
}

public sealed class EmailOptions
{
    public string? Host { get; set; }
    public int Port { get; set; } = 587;
    public string? Sender { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
}
