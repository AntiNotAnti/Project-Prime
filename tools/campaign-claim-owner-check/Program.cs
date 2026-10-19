using System.Reflection;
using System.Text.Json;
using MphRead.Mods.Network;

int checks = 0;
void Check(bool value, string name) { if (!value) throw new InvalidOperationException(name); checks++; }
bool enabled = args.Length == 1 && args[0] == "enabled";
MphRead.Mods.Headless.Enter();
Check(NetCampaignEvidence.Enabled == enabled, "process environment explicitly selects evidence mode before owner initializes");
JsonElement Claims() => JsonSerializer.SerializeToElement(NetCampaignEvidence.CaptureClaims());
long Count(string field) => Claims().GetProperty(field).GetInt64();
Check(Count("ClientDeclared") == 0 && Count("Received") == 0 && Count("Applied") == 0, "actual process owner begins with zero counters");
Check(Claims().GetProperty("ClientDeclaredScope").GetString() == "ProcessLifetime"
    && Claims().GetProperty("ClientDeclaredCountingUnit").GetString() == "New client claim declarations; excludes retries and survives match reset",
    "actual owner publishes explicit counting unit and scope");
long expected = 0;
for (int round = 0; round < 6; round++)
{
    for (int declaration = 0; declaration <= round; declaration++) NetCampaignEvidence.ClaimDeclared();
    if (enabled) expected += round + 1;
    Check(Count("ClientDeclared") == expected, "diagnostic new-declaration count before round reset " + round);
    // This sets only the existing diagnostic scalar, then exercises the real
    // production reset. It does not manufacture gameplay claims or world data.
    typeof(NetHitClaims).GetProperty(nameof(NetHitClaims.Declared))!.SetValue(null, 17L);
    NetHitClaims.Reset();
    Check(NetHitClaims.Declared == 0 && Count("ClientDeclared") == expected,
        "actual per-match reset retires its scalar without erasing process diagnostic " + round);
}
NetCampaignEvidence.ClaimReceived(); NetCampaignEvidence.ClaimReceived(); NetCampaignEvidence.ClaimApplied();
Check(Count("Received") == (enabled ? 2 : 0) && Count("Applied") == (enabled ? 1 : 0), "existing authority lifetime counters preserve meaning");
Check(Count("ClientDeclared") == expected, "authority receipt or application does not manufacture a client declaration");
NetHitClaims.Reset();
Check(Count("ClientDeclared") == expected && Count("Received") == (enabled ? 2 : 0)
    && Count("Applied") == (enabled ? 1 : 0), "all process counters survive repeated production reset");
Check(!NetSession.Active, "counter/reset regression creates no network session");
Console.WriteLine($"PASS {checks} actual compiled campaign claim owner checks; evidence={enabled}, no socket/world/assets/backend; declaration gameplay exercised by separate real-client campaign");
