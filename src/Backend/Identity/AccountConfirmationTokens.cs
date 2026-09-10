using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace MphRead.Backend.Identity;

public sealed class AccountConfirmationTokens
{
    private const int TimestampBytes = sizeof(long);
    private readonly IDataProtector _protector;
    private readonly TimeProvider _clock;
    private readonly TimeSpan _lifetime;

    public AccountConfirmationTokens(IDataProtectionProvider protection, IOptions<AccountOptions> options,
        TimeProvider clock)
    {
        int minutes = options.Value.ConfirmationTokenLifetimeMinutes;
        if (minutes is < 5 or > 24 * 60)
            throw new InvalidOperationException("Confirmation token lifetime must be between 5 minutes and 24 hours.");
        _protector = protection.CreateProtector("ProjectPrime.AccountConfirmation.v1");
        _clock = clock;
        _lifetime = TimeSpan.FromMinutes(minutes);
    }

    public string Protect(string identityToken)
    {
        if (identityToken is not { Length: >= 1 and <= 2_048 })
            throw new InvalidOperationException("Identity confirmation token exceeded its expected bound.");
        byte[] token = Encoding.UTF8.GetBytes(identityToken);
        byte[] payload = new byte[TimestampBytes + token.Length];
        BinaryPrimitives.WriteInt64BigEndian(payload, _clock.GetUtcNow().ToUnixTimeSeconds());
        token.CopyTo(payload, TimestampBytes);
        return Convert.ToBase64String(_protector.Protect(payload));
    }

    public bool TryUnprotect(string protectedToken, out string identityToken)
    {
        identityToken = "";
        try
        {
            byte[] payload = _protector.Unprotect(Convert.FromBase64String(protectedToken));
            if (payload.Length <= TimestampBytes) return false;
            DateTimeOffset issuedAt = DateTimeOffset.FromUnixTimeSeconds(
                BinaryPrimitives.ReadInt64BigEndian(payload.AsSpan(0, TimestampBytes)));
            TimeSpan age = _clock.GetUtcNow() - issuedAt;
            if (age < TimeSpan.Zero || age >= _lifetime) return false;
            identityToken = Encoding.UTF8.GetString(payload, TimestampBytes, payload.Length - TimestampBytes);
            return identityToken.Length > 0;
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException or ArgumentException)
        {
            return false;
        }
    }
}
