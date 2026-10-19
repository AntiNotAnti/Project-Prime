#if MPHREAD_AVALONIA
using System;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.Launcher;

internal sealed class AuthenticatedNativePresenceService : INativePresenceService
{
    public bool IsCurrent(NativePresenceDirectory directory)
        => HunterLicenseClient.IsNativeAccountLeaseCurrent(directory.AccountWitness);
    public Task<NativePresenceDirectory> ReadAsync(CancellationToken token)
        => Send(new() { Action = "snapshot" },token);
    public Task<NativePresenceDirectory> HeartbeatAsync(string self, Guid session, string activity, string room, CancellationToken token)
        => Send(new() { Action = "heartbeat", Self = self, Session = session.ToString(), Activity = activity, Room = room },token);
    public Task<NativePresenceDirectory> PrivacyAsync(string self, NativeSocialPrivacy expected, NativeSocialPrivacy next, CancellationToken token)
        => Send(new() { Action = "set_privacy", Self = self, Expected = WirePrivacy.From(expected), Settings = WirePrivacy.From(next) },token);
    public async Task LeaveAsync(string self, Guid session, CancellationToken token)
        => _ = await Send(new() { Action = "leave", Self = self, Session = session.ToString() },token).ConfigureAwait(false);
    private static async Task<NativePresenceDirectory> Send(Request request, CancellationToken token)
    {
        string name = LauncherPrefs.PlayerName.Trim();
        request.Name = name.Length == 0 ? "Player" : name;
        request.Hunter = Math.Clamp((int)Hunters.Resolve(LauncherPrefs.LastHunter),0,6);
        var leased = await HunterLicenseClient.InvokeNativeFunctionWithLeaseAsync<Envelope>(
            "native-presence",request,token).ConfigureAwait(false);
        Envelope result = leased.Value;
        if (!result.Ok || result.Snapshot == null) throw new InvalidOperationException("Presence was not authorized.");
        if (!HunterLicenseClient.IsNativeAccountLeaseCurrent(leased.Lease))
            throw new InvalidOperationException("Presence account changed.");
        return Parse(result.Snapshot) with { AccountWitness = leased.Lease };
    }
    internal static NativePresenceDirectory Parse(WireSnapshot wire)
    {
        if (wire.Settings == null || wire.Settings.Dnd == null || wire.Players == null || wire.Players.Length > 1024
            || wire.Players.Any(p => p == null || p.Joinable == null)
            || wire.ExpiresAfterSeconds != 45) throw new InvalidOperationException("Invalid presence data.");
        var data = new NativePresenceDirectory(wire.Self?.ToUpperInvariant() ?? "",
            new(wire.Settings.Presence,wire.Settings.Activity,wire.Settings.Invites,wire.Settings.Dnd.Value),
            wire.Players.Select(p => p == null ? null! : new NativePresencePerson(p.PrimeId?.ToUpperInvariant() ?? "",
                p.Name,p.Activity,p.Room ?? "",p.Lobby ?? "",p.Joinable!.Value,p.LastSeen)).ToArray());
        if (!NativePresenceController.ValidDirectory(data)) throw new InvalidOperationException("Invalid presence data.");
        return data;
    }
    internal sealed class Request
    {
        [JsonPropertyName("action")] public string Action { get; set; } = "";
        [JsonPropertyName("expected_self_prime_id")] public string Self { get; set; } = "";
        [JsonPropertyName("session_id")] public string Session { get; set; } = "";
        [JsonPropertyName("activity")] public string Activity { get; set; } = "";
        [JsonPropertyName("room_key")] public string Room { get; set; } = "";
        [JsonPropertyName("expected_settings")] public WirePrivacy? Expected { get; set; }
        [JsonPropertyName("settings")] public WirePrivacy? Settings { get; set; }
        [JsonPropertyName("display_name")] public string Name { get; set; } = "";
        [JsonPropertyName("favorite_hunter")] public int Hunter { get; set; }
    }
    internal sealed class Envelope
    {
        [JsonPropertyName("ok")] public bool Ok { get; set; }
        [JsonPropertyName("snapshot")] public WireSnapshot? Snapshot { get; set; }
    }
    internal sealed class WireSnapshot
    {
        [JsonPropertyName("self_prime_id")] public string Self { get; set; } = "";
        [JsonPropertyName("settings")] public WirePrivacy? Settings { get; set; }
        [JsonPropertyName("players")] public WirePerson[]? Players { get; set; }
        [JsonPropertyName("expires_after_seconds")] public int ExpiresAfterSeconds { get; set; }
    }
    internal sealed class WirePrivacy
    {
        [JsonPropertyName("presence_visibility")] public string Presence { get; set; } = "";
        [JsonPropertyName("activity_visibility")] public string Activity { get; set; } = "";
        [JsonPropertyName("invite_policy")] public string Invites { get; set; } = "";
        [JsonPropertyName("do_not_disturb")] public bool? Dnd { get; set; }
        public static WirePrivacy From(NativeSocialPrivacy p) => new() { Presence=p.Presence,Activity=p.Activity,Invites=p.Invites,Dnd=p.DoNotDisturb };
    }
    internal sealed class WirePerson
    {
        [JsonPropertyName("prime_id")] public string PrimeId { get; set; } = "";
        [JsonPropertyName("display_name")] public string Name { get; set; } = "";
        [JsonPropertyName("activity")] public string Activity { get; set; } = "";
        [JsonPropertyName("room_key")] public string? Room { get; set; }
        [JsonPropertyName("lobby_id")] public string? Lobby { get; set; }
        [JsonPropertyName("joinable")] public bool? Joinable { get; set; }
        [JsonPropertyName("last_seen")] public DateTimeOffset LastSeen { get; set; }
    }
}
#endif
