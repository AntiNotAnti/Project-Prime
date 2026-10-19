#if MPHREAD_AVALONIA
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.Launcher;

internal static partial class HunterLicenseClient
{
    /// <summary>Run a bounded local publication while the existing auth owner
    /// still holds this exact account. The action performs no HTTPS or awaits.</summary>
    internal static async Task<bool> ExecuteNativeLeaseActionAsync(object? expectedLease,
        Func<bool> action, CancellationToken token)
    {
        await Gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            token.ThrowIfCancellationRequested();
            if (!IsNativeAccountLeaseCurrent(expectedLease)) return false;
            return action();
        }
        finally { Gate.Release(); }
    }
    // A process-local reference only. No token or private account identifier
    // leaves the existing auth owner. Refresh conservatively retires the lease.
    internal static bool IsNativeAccountLeaseCurrent(object? lease)
    {
        AuthSession? current = Volatile.Read(ref _currentSession);
        return lease != null && ReferenceEquals(current, lease)
            && current!.ExpiresAt > DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }

    internal static async Task<(T Value, object Lease)> InvokeNativeFunctionWithLeaseAsync<T>(
        string function, object body, CancellationToken token, object? expectedLease = null)
    {
        if (String.IsNullOrWhiteSpace(function) || function.Length > 64
            || !function.All(c => Char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
            throw new ArgumentException("Invalid authenticated function name.", nameof(function));
        await Gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (expectedLease != null && !IsNativeAccountLeaseCurrent(expectedLease))
                throw new InvalidOperationException("Authenticated account changed.");
            AuthSession session = await AuthenticateAsync(token).ConfigureAwait(false);
            if (expectedLease != null && (!ReferenceEquals(session, expectedLease)
                || !IsNativeAccountLeaseCurrent(expectedLease)))
                throw new InvalidOperationException("Authenticated account changed.");
            T value = await FunctionAsync<T>(session.AccessToken, function, body, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            return (value, session);
        }
        finally { Gate.Release(); }
    }
}
#endif
