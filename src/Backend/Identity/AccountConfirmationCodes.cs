using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using MphRead.Backend.Data;

namespace MphRead.Backend.Identity;

/// <summary>
/// Issues short, high-entropy, single-use email confirmation codes. Only a
/// SHA-256 digest and issue time are stored in the existing Identity token
/// table; issuing a replacement immediately invalidates the previous code.
/// </summary>
public sealed class AccountConfirmationCodes
{
    private const string LoginProvider = "ProjectPrime";
    private const string TokenName = "EmailConfirmationCode";
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    private const int CodeCharacters = 12;
    private readonly TimeProvider _clock;
    private readonly TimeSpan _lifetime;

    public AccountConfirmationCodes(IOptions<AccountOptions> options, TimeProvider clock)
    {
        int minutes = options.Value.ConfirmationTokenLifetimeMinutes;
        if (minutes is < 5 or > 24 * 60)
            throw new InvalidOperationException("Confirmation token lifetime must be between 5 minutes and 24 hours.");
        _clock = clock;
        _lifetime = TimeSpan.FromMinutes(minutes);
    }

    public async Task<string> IssueAsync(UserManager<HunterAccount> users, HunterAccount user)
    {
        byte[] random = RandomNumberGenerator.GetBytes(CodeCharacters);
        char[] canonical = new char[CodeCharacters];
        for (int index = 0; index < canonical.Length; index++)
        {
            // Alphabet.Length is 32, so this mapping introduces no modulo bias.
            canonical[index] = Alphabet[random[index] & 31];
        }
        string value = new(canonical);
        string record = string.Create(CultureInfo.InvariantCulture,
            $"v1:{_clock.GetUtcNow().ToUnixTimeSeconds()}:{Hash(value)}");
        IdentityResult result = await users.SetAuthenticationTokenAsync(user,
            LoginProvider, TokenName, record);
        if (!result.Succeeded)
            throw new InvalidOperationException("The confirmation code could not be stored.");
        return $"{value[..4]}-{value[4..8]}-{value[8..]}";
    }

    public async Task<bool> VerifyAsync(UserManager<HunterAccount> users,
        HunterAccount user, string supplied)
    {
        if (!TryNormalize(supplied, out string canonical)) return false;
        string? record = await users.GetAuthenticationTokenAsync(user, LoginProvider, TokenName);
        if (record == null) return false;
        string[] fields = record.Split(':', StringSplitOptions.None);
        if (fields.Length != 3 || fields[0] != "v1"
            || !long.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out long issuedSeconds)
            || fields[2].Length != 64)
        {
            return false;
        }
        DateTimeOffset issuedAt;
        byte[] expected;
        try
        {
            issuedAt = DateTimeOffset.FromUnixTimeSeconds(issuedSeconds);
            expected = Convert.FromHexString(fields[2]);
        }
        catch (Exception exception) when (exception is ArgumentOutOfRangeException or FormatException)
        {
            return false;
        }
        TimeSpan age = _clock.GetUtcNow() - issuedAt;
        return age >= TimeSpan.Zero && age < _lifetime
            && CryptographicOperations.FixedTimeEquals(expected,
                Convert.FromHexString(Hash(canonical)));
    }

    public async Task<bool> ConsumeAsync(UserManager<HunterAccount> users, HunterAccount user)
        => (await users.RemoveAuthenticationTokenAsync(user, LoginProvider, TokenName)).Succeeded;

    private static bool TryNormalize(string supplied, out string canonical)
    {
        var normalized = new StringBuilder(CodeCharacters);
        foreach (char character in supplied)
        {
            if (character == '-' || char.IsWhiteSpace(character)) continue;
            char upper = char.ToUpperInvariant(character);
            if (!Alphabet.Contains(upper, StringComparison.Ordinal)
                || normalized.Length == CodeCharacters)
            {
                canonical = "";
                return false;
            }
            normalized.Append(upper);
        }
        canonical = normalized.ToString();
        return canonical.Length == CodeCharacters;
    }

    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(value)));
}
