using System;
using System.Reflection;
using System.Text.Json;
using MphRead.Mods.Launcher;

internal static class PartyLeaseAdapterAcceptance
{
    internal static void Run(AuthenticatedNativePartyAuthority.PartyWireSnapshot wire)
    {
        Type owner = typeof(HunterLicenseClient);
        FieldInfo sessionField = owner.GetField("_currentSession", BindingFlags.Static | BindingFlags.NonPublic)!;
        Type sessionType = owner.GetNestedType("AuthSession", BindingFlags.NonPublic)!;
        object? previous = sessionField.GetValue(null);
        long future = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 3600;
        object Session()
        {
            object value = Activator.CreateInstance(sessionType, nonPublic: true)!;
            sessionType.GetProperty("ExpiresAt")!.SetValue(value, future);
            sessionType.GetProperty("AccessToken")!.SetValue(value, "SYNTHETIC-PARTY-ACCESS-PRIVATE");
            sessionType.GetProperty("RefreshToken")!.SetValue(value, "SYNTHETIC-PARTY-REFRESH-PRIVATE");
            return value;
        }
        var service = new AuthenticatedNativePartyAuthority();
        try
        {
            object first = Session(), replacement = Session();
            sessionField.SetValue(null, first);
            NativePartySnapshot witnessed = AuthenticatedNativePartyAuthority.ParseLeased(wire, first);
            Check(ReferenceEquals(witnessed.AccountWitness, first) && service.IsCurrent(witnessed),
                "production Party projection preserves the exact real auth lease without an HTTP call");
            string json = JsonSerializer.Serialize(witnessed);
            Check(!json.Contains("AccountWitness", StringComparison.Ordinal)
                && !json.Contains("SYNTHETIC-PARTY-ACCESS-PRIVATE", StringComparison.Ordinal)
                && !json.Contains("SYNTHETIC-PARTY-REFRESH-PRIVATE", StringComparison.Ordinal),
                "real opaque Party auth-session reference never serializes credentials or witness fields");
            sessionField.SetValue(null, replacement);
            Check(!service.IsCurrent(witnessed) && Rejects(() => AuthenticatedNativePartyAuthority.ParseLeased(wire, first)),
                "equal credential values in a replacement session retire old Party reads and cached state");
            Check(Rejects(() => HunterLicenseClient.InvokeNativeFunctionWithLeaseAsync<object>(
                "party", new { action = "snapshot" }, default, first).GetAwaiter().GetResult()),
                "captured old lease rejects authenticated delegate before any refresh or HTTPS");
            NativePartySnapshot fresh = AuthenticatedNativePartyAuthority.ParseLeased(wire, replacement);
            Check(service.IsCurrent(fresh), "fresh production projection accepts the new current session only");
            sessionType.GetProperty("ExpiresAt")!.SetValue(replacement, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            Check(!service.IsCurrent(fresh) && Rejects(() => AuthenticatedNativePartyAuthority.ParseLeased(wire, replacement)),
                "current-second expiry retires Party authority and late read parsing without network activity");
            Check(Rejects(() => HunterLicenseClient.InvokeNativeFunctionWithLeaseAsync<object>(
                "party", new { action = "snapshot" }, default, replacement).GetAwaiter().GetResult()),
                "expired captured lease fails before refresh or authenticated delegate");
            sessionType.GetProperty("ExpiresAt")!.SetValue(replacement, future);
            Check(!service.IsCurrent(AuthenticatedNativePartyAuthority.Parse(wire)),
                "unwitnessed parser output cannot impersonate an authenticated production Party response");
            sessionField.SetValue(null, null);
            Check(!service.IsCurrent(fresh) && Rejects(() => AuthenticatedNativePartyAuthority.ParseLeased(wire, replacement)),
                "cleared session rejects all cached or late Party witnesses");
            Check(!service.ExecuteAsync(new(NativePartyAction.Leave, -1, wire.PartyId, "", "", wire.SelfPrimeId), default).GetAwaiter().GetResult()
                && !service.ExecuteAsync(new(NativePartyAction.Leave, 10, wire.PartyId, "", "", ""), default).GetAwaiter().GetResult(),
                "mandatory expected revision and self checks still reject before authentication or HTTPS");
        }
        finally { sessionField.SetValue(null, previous); }
    }
    private static bool Rejects(Action action)
    {
        try { action(); return false; }
        catch (InvalidOperationException) { return true; }
    }
    private static void Check(bool value, string label)
    {
        if (!value) throw new InvalidOperationException(label);
        Console.WriteLine("RML PARTY LEASE ADAPTER PASS " + label);
    }
}
