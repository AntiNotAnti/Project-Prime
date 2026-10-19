using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Launcher;

internal static class PresenceAdapterAcceptance
{
    internal static void Run()
    {
        string self = PartyGlAcceptance.Id(1), other = PartyGlAcceptance.Id(2);
        string validJson = JsonSerializer.Serialize(new
        {
            self_prime_id = self.ToLowerInvariant(),
            settings = new { presence_visibility = "hidden", activity_visibility = "private", invite_policy = "nobody", do_not_disturb = true },
            players = new[] { new { prime_id = other.ToLowerInvariant(), display_name = "SYNTHETIC PRESENCE", activity = "online",
                room_key = (string?)null, lobby_id = (string?)null, joinable = false, last_seen = DateTimeOffset.UtcNow } },
            expires_after_seconds = 45
        });
        AuthenticatedNativePresenceService.WireSnapshot Wire(string json) => JsonSerializer.Deserialize<AuthenticatedNativePresenceService.WireSnapshot>(json)!;
        var parsed = AuthenticatedNativePresenceService.Parse(Wire(validJson));
        Check(parsed.SelfPrimeId == self && parsed.People[0].PrimeId == other
            && parsed.People[0].RoomKey == "" && parsed.People[0].LobbyId == ""
            && parsed.Privacy == new NativeSocialPrivacy("hidden", "private", "nobody", true),
            "production Presence parser canonicalizes IDs and preserves privacy redaction without inferred offline state");
        foreach (string malformed in new[]
        {
            "{}", validJson.Replace("\"players\":", "\"missing_players\":"),
            validJson.Replace("\"expires_after_seconds\":45", "\"expires_after_seconds\":46"),
            validJson.Replace("\"do_not_disturb\":true", "\"missing_do_not_disturb\":true"),
            validJson.Replace("\"joinable\":false", "\"missing_joinable\":false"),
            validJson.Replace("\"activity\":\"online\"", "\"activity\":\"offline\""),
            validJson.Replace("\"joinable\":false", "\"joinable\":true"),
            validJson.Replace(other.ToLowerInvariant(), self.ToLowerInvariant())
        })
        {
            bool rejected = false;
            try { AuthenticatedNativePresenceService.Parse(Wire(malformed)); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "production Presence parser rejects absent required fields, wrong TTL, unsafe membership or fabricated activity");
        }
        var request = new AuthenticatedNativePresenceService.Request
        {
            Action = "set_privacy", Self = self,
            Expected = AuthenticatedNativePresenceService.WirePrivacy.From(new("everyone", "friends", "friends", false)),
            Settings = AuthenticatedNativePresenceService.WirePrivacy.From(new("hidden", "private", "nobody", true))
        };
        using var encoded = JsonDocument.Parse(JsonSerializer.Serialize(request));
        Check(encoded.RootElement.GetProperty("expected_self_prime_id").GetString() == self
            && encoded.RootElement.GetProperty("expected_settings").GetProperty("presence_visibility").GetString() == "everyone"
            && encoded.RootElement.GetProperty("settings").GetProperty("do_not_disturb").GetBoolean()
            && !encoded.RootElement.EnumerateObject().Any(property => property.Name.Contains("token", StringComparison.OrdinalIgnoreCase)),
            "production Presence wire request retains captured self and exact expected/next privacy without credential fields");
        AccountLeases(parsed);
    }

    private static void AccountLeases(NativePresenceDirectory directory)
    {
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        Type owner = typeof(HunterLicenseClient);
        FieldInfo sessionField = owner.GetField("_currentSession", flags)!;
        FieldInfo socialField = typeof(SocialClient).GetField("_current", flags)!;
        Type sessionType = owner.GetNestedType("AuthSession", BindingFlags.NonPublic)!;
        object? previousSession = sessionField.GetValue(null);
        object? previousSocial = socialField.GetValue(null);
        long future = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 3600;
        object Session(long expires)
        {
            object value = Activator.CreateInstance(sessionType, nonPublic: true)!;
            sessionType.GetProperty("ExpiresAt")!.SetValue(value, expires);
            sessionType.GetProperty("AccessToken")!.SetValue(value, "SYNTHETIC-ACCESS-DO-NOT-PROJECT");
            sessionType.GetProperty("RefreshToken")!.SetValue(value, "SYNTHETIC-REFRESH-DO-NOT-PROJECT");
            return value;
        }
        var service = new AuthenticatedNativePresenceService();
        try
        {
            object first = Session(future), replacement = Session(future);
            sessionField.SetValue(null, first);
            Check(HunterLicenseClient.IsNativeAccountLeaseCurrent(first)
                && !HunterLicenseClient.IsNativeAccountLeaseCurrent(null)
                && !HunterLicenseClient.IsNativeAccountLeaseCurrent(new object())
                && !HunterLicenseClient.IsNativeAccountLeaseCurrent(replacement),
                "real auth lease accepts only the exact current unexpired session reference");
            using (var cancelled = new CancellationTokenSource())
            {
                cancelled.Cancel();
                bool rejected = false;
                try
                {
                    HunterLicenseClient.InvokeNativeFunctionWithLeaseAsync<object>("native-presence", new { }, cancelled.Token)
                        .GetAwaiter().GetResult();
                }
                catch (OperationCanceledException) { rejected = true; }
                Check(rejected && ReferenceEquals(sessionField.GetValue(null), first),
                    "pre-cancelled native function lease rejects before authentication or session replacement");
            }
            var social = new SocialSnapshot { AccountWitness = first,
                Self = new SocialPlayer { PrimeId = directory.SelfPrimeId } };
            socialField.SetValue(null, social);
            var witnessed = directory with { AccountWitness = first };
            Check(ReferenceEquals(SocialClient.Current, social) && service.IsCurrent(witnessed),
                "real Social cache and Presence adapter recognize the current opaque session witness");
            string serialized = JsonSerializer.Serialize(witnessed) + JsonSerializer.Serialize(social);
            Check(!serialized.Contains("SYNTHETIC-ACCESS-DO-NOT-PROJECT", StringComparison.Ordinal)
                && !serialized.Contains("SYNTHETIC-REFRESH-DO-NOT-PROJECT", StringComparison.Ordinal)
                && !serialized.Contains("AccountWitness", StringComparison.Ordinal),
                "opaque Social and Presence witnesses never serialize session credentials or lease fields");

            sessionField.SetValue(null, replacement);
            Check(!HunterLicenseClient.IsNativeAccountLeaseCurrent(first)
                && HunterLicenseClient.IsNativeAccountLeaseCurrent(replacement)
                && SocialClient.Current == null && !service.IsCurrent(witnessed),
                "replacing session with identical credential values immediately redacts old Social and Presence data");
            social.AccountWitness = replacement;
            Check(ReferenceEquals(SocialClient.Current, social),
                "a freshly witnessed Social cache can be read for the replacement session");
            sessionType.GetProperty("ExpiresAt")!.SetValue(replacement, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            Check(!HunterLicenseClient.IsNativeAccountLeaseCurrent(replacement)
                && SocialClient.Current == null
                && !service.IsCurrent(directory with { AccountWitness = replacement }),
                "session expiry at the current second retires cached authority without a network call");
            sessionType.GetProperty("ExpiresAt")!.SetValue(replacement, future);
            social.AccountWitness = null;
            Check(SocialClient.Current == null && !service.IsCurrent(directory),
                "unwitnessed cached responses have no authenticated local authority");
            sessionField.SetValue(null, null);
            Check(!HunterLicenseClient.IsNativeAccountLeaseCurrent(first)
                && !HunterLicenseClient.IsNativeAccountLeaseCurrent(replacement),
                "clearing current session invalidates every previously issued witness");

            sessionField.SetValue(null, first);
            var delayed = new WitnessService(service);
            using (var controller = new NativePresenceController(delayed))
            {
                controller.Refresh();
                sessionField.SetValue(null, replacement);
                delayed.Read.SetResult(witnessed);
                NativePresenceView result = Complete(controller);
                Check(result.Directory == null && result.Status.Contains("UNAVAILABLE", StringComparison.Ordinal)
                    && !controller.SetPrivacy(new("everyone", "everyone", "everyone", false)),
                    "a delayed old-account Presence response cannot publish data or admit privacy writes");
            }
            var live = new WitnessService(service);
            using (var controller = new NativePresenceController(live))
            {
                controller.Refresh();
                live.Read.SetResult(directory with { AccountWitness = replacement });
                NativePresenceView accepted = Complete(controller);
                Check(accepted.Directory != null, "current witnessed Presence response publishes through the real controller");
                sessionField.SetValue(null, first);
                NativePresenceView retired = controller.Current();
                Check(retired.Directory == null && retired.Revision > accepted.Revision
                    && retired.Status.Contains("ACCOUNT CHANGED", StringComparison.Ordinal)
                    && !controller.SetPrivacy(new("everyone", "everyone", "everyone", false))
                    && live.Mutations == 0,
                    "Current synchronously redacts previously returned Presence data before privacy admission after account replacement");
            }
        }
        finally
        {
            socialField.SetValue(null, previousSocial);
            sessionField.SetValue(null, previousSession);
        }
        Check(ReferenceEquals(sessionField.GetValue(null), previousSession)
            && ReferenceEquals(socialField.GetValue(null), previousSocial),
            "reflection-only synthetic auth fixture restores both static owner and Social cache references");
    }

    private static NativePresenceView Complete(NativePresenceController controller)
    {
        NativePresenceView? result = null;
        if (!SpinWait.SpinUntil(() => { result = controller.Tick(); return !result.Busy; }, TimeSpan.FromSeconds(3)))
            throw new InvalidOperationException("Synthetic Presence completion timed out.");
        return result!;
    }

    private sealed class WitnessService(AuthenticatedNativePresenceService authority) : INativePresenceService
    {
        internal readonly TaskCompletionSource<NativePresenceDirectory> Read = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Mutations;
        public bool IsCurrent(NativePresenceDirectory value) => authority.IsCurrent(value);
        public Task<NativePresenceDirectory> ReadAsync(CancellationToken token) => Read.Task;
        public Task<NativePresenceDirectory> HeartbeatAsync(string self, Guid session, string activity, string room, CancellationToken token)
            => throw new InvalidOperationException("Synthetic heartbeat must not run.");
        public Task<NativePresenceDirectory> PrivacyAsync(string self, NativeSocialPrivacy expected, NativeSocialPrivacy next, CancellationToken token)
        { ++Mutations; throw new InvalidOperationException("Synthetic privacy write must not run."); }
        public Task LeaveAsync(string self, Guid session, CancellationToken token) => Task.CompletedTask;
    }
    private static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException(label);
        Console.WriteLine("RML PRESENCE ADAPTER PASS " + label);
    }
}
