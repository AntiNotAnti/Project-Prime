using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace MphRead.Backend.Identity;

public sealed class ConfirmationResendLimiter
{
    private sealed class Window
    {
        public DateTimeOffset StartedAt;
        public int Count;
    }

    private readonly Lock _lock = new();
    private readonly Dictionary<string, Window> _accounts = [];
    private readonly Dictionary<string, Window> _addresses = [];
    private readonly byte[] _accountHashKey = RandomNumberGenerator.GetBytes(32);
    private readonly TimeProvider _clock;
    private readonly TimeSpan _window;
    private readonly int _perAccount;
    private readonly int _perIp;
    private readonly int _maximumEntries;

    public ConfirmationResendLimiter(IOptions<AccountOptions> options, TimeProvider clock)
    {
        AccountOptions settings = options.Value;
        if (settings.ConfirmationResendWindowSeconds is < 10 or > 24 * 60 * 60
            || settings.ConfirmationResendsPerAccount is < 1 or > 100
            || settings.ConfirmationResendsPerIp is < 1 or > 1_000
            || settings.ConfirmationResendTrackedEntries is < 100 or > 1_000_000)
            throw new InvalidOperationException("Confirmation resend limits are outside their safe bounds.");
        _clock = clock;
        _window = TimeSpan.FromSeconds(settings.ConfirmationResendWindowSeconds);
        _perAccount = settings.ConfirmationResendsPerAccount;
        _perIp = settings.ConfirmationResendsPerIp;
        _maximumEntries = settings.ConfirmationResendTrackedEntries;
    }

    public bool TryAcquire(IPAddress? remoteAddress, string normalizedEmail)
    {
        string account = Convert.ToHexString(HMACSHA256.HashData(_accountHashKey,
            Encoding.UTF8.GetBytes(normalizedEmail)));
        string address = remoteAddress?.MapToIPv6().ToString() ?? "unknown";
        DateTimeOffset now = _clock.GetUtcNow();
        lock (_lock)
        {
            int requiredEntries = (_accounts.ContainsKey(account) ? 0 : 1) + (_addresses.ContainsKey(address) ? 0 : 1);
            if (!HasCapacity(requiredEntries, now)) return false;
            Window accountWindow = Current(_accounts, account, now);
            Window addressWindow = Current(_addresses, address, now);
            if (accountWindow.Count >= _perAccount || addressWindow.Count >= _perIp) return false;
            accountWindow.Count++;
            addressWindow.Count++;
            return true;
        }
    }

    private Window Current(Dictionary<string, Window> entries, string key, DateTimeOffset now)
    {
        if (!entries.TryGetValue(key, out Window? value) || now - value.StartedAt >= _window)
        {
            value = new Window { StartedAt = now };
            entries[key] = value;
        }
        return value;
    }

    private bool HasCapacity(int requiredEntries, DateTimeOffset now)
    {
        if (_accounts.Count + _addresses.Count + requiredEntries <= _maximumEntries) return true;
        DateTimeOffset oldest = now - _window;
        foreach (string key in _accounts.Where(item => item.Value.StartedAt <= oldest).Select(item => item.Key).ToArray())
            _accounts.Remove(key);
        foreach (string key in _addresses.Where(item => item.Value.StartedAt <= oldest).Select(item => item.Key).ToArray())
            _addresses.Remove(key);
        return _accounts.Count + _addresses.Count + requiredEntries <= _maximumEntries;
    }
}
