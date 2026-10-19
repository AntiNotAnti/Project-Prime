using System;
using System.Linq;
using System.Text.Json;
using MphRead.Mods.Launcher;

internal static class PartyAdapterAcceptance
{
    internal static void Run()
    {
        PresenceAdapterAcceptance.Run();
        string id = PartyGlAcceptance.Id(1), member = PartyGlAcceptance.Id(2);
        string party = PartyGlAcceptance.Uuid(1);
        string validJson = JsonSerializer.Serialize(new
        {
            self_prime_id = id, party_id = party, leader_prime_id = id, server_revision = 12,
            members = new[] { new { prime_id = id, display_name = "SYNTHETIC SELF", presence = "unknown" },
                new { prime_id = member, display_name = "SYNTHETIC MEMBER", presence = "match" } },
            incoming = new[] { new { invite_id = PartyGlAcceptance.Uuid(11), party_id = PartyGlAcceptance.Uuid(2),
                from_prime_id = PartyGlAcceptance.Id(3), to_prime_id = id, expires_at = DateTimeOffset.UtcNow.AddMinutes(10) } },
            outgoing = Array.Empty<object>()
        });
        AuthenticatedNativePartyAuthority.PartyWireSnapshot Wire(string json) =>
            JsonSerializer.Deserialize<AuthenticatedNativePartyAuthority.PartyWireSnapshot>(json)!;
        var parsed = AuthenticatedNativePartyAuthority.Parse(Wire(validJson));
        Check(parsed.SelfPrimeId == id && parsed.Members[1].Presence == NativePresence.InMatch
            && parsed.Members[0].Presence == NativePresence.Unknown && parsed.Incoming[0].PartyId == PartyGlAcceptance.Uuid(2),
            "production Party adapter parses exact flat REST snapshot and privacy-safe unknown presence");
        foreach (string malformed in new[]
        {
            "{}", validJson.Replace("\"members\":", "\"missing_members\":"),
            validJson.Replace("\"server_revision\":12", "\"server_revision\":9007199254740992"),
            validJson.Replace("\"party_id\":\"" + party + "\"", "\"party_id\":\"not-a-uuid\""),
            validJson.Replace("\"leader_prime_id\":\"" + id + "\"", "\"leader_prime_id\":\"" + PartyGlAcceptance.Id(9) + "\""),
            validJson.Replace("\"to_prime_id\":\"" + id + "\"", "\"to_prime_id\":\"" + member + "\"")
        })
        {
            bool rejected = false;
            try { AuthenticatedNativePartyAuthority.Parse(Wire(malformed)); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "production Party parser rejects missing fields, unsafe revision and inconsistent authority identities");
        }
        var request = new AuthenticatedNativePartyAuthority.PartyWireRequest
        {
            Action = "accept_invite", ExpectedRevision = 12, ExpectedSelfPrimeId = id,
            PartyId = PartyGlAcceptance.Uuid(2), InviteId = PartyGlAcceptance.Uuid(11)
        };
        using var encoded = JsonDocument.Parse(JsonSerializer.Serialize(request));
        Check(encoded.RootElement.GetProperty("expected_self_prime_id").GetString() == id
            && encoded.RootElement.GetProperty("expected_revision").GetInt64() == 12
            && encoded.RootElement.GetProperty("party_id").GetString() == PartyGlAcceptance.Uuid(2)
            && !encoded.RootElement.EnumerateObject().Any(property => property.Name.Contains("token", StringComparison.OrdinalIgnoreCase)),
            "production wire request serializes exact self/revision/invitation-party witness without credential fields");
        PartyLeaseAdapterAcceptance.Run(Wire(validJson));
    }
    private static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException(label);
        Console.WriteLine("RML PARTY ADAPTER PASS " + label);
    }
}
