using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

// Process-isolated accounting regression. The decoded claim is deliberately
// lifecycle-invalid: it must count as a receipt and never authorize gameplay.
internal static class CampaignClaimEvidenceTests
{
    private static int _checks;
    private static void Check(bool value, string message)
    {
        _checks++;
        if (!value) throw new InvalidOperationException("Campaign claim accounting: " + message);
    }
    internal static int Run()
    {
        _checks = 0;
        string? previous = Environment.GetEnvironmentVariable("PRIME_NET2_EVIDENCE");
        string directory = Path.Combine(Path.GetTempPath(), "prime-unit-claim-evidence-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string output = Path.Combine(directory, "invalid-claim-unit.json");
        try
        {
            // Evidence owns its opt-in at type initialization. Keep its first
            // access in a separate, non-inlined method after setting the path.
            Environment.SetEnvironmentVariable("PRIME_NET2_EVIDENCE", output);
            Execute(output);
            Console.WriteLine($"PASS: campaign claim accounting {_checks} assertions; real invalid claim receipts survive teardown reset, no gameplay application.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally
        {
            Environment.SetEnvironmentVariable("PRIME_NET2_EVIDENCE", previous);
            Directory.Delete(directory, recursive: true);
        }
    }
    private static JsonElement Claims() => JsonSerializer.SerializeToElement(NetCampaignEvidence.CaptureClaims());

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Execute(string output)
    {
        var previousRole = NetSession.Role; bool previousAuthority = NetSession.IsAuthority;
        var previousMatch = NetSession.ServerMatch;
        uint previousFrame = NetSession.NetFrame;
        bool previousEnabled = NetHitClaims.Enabled;
        long previousOldLife = NetPlayerLifecycle.OldLifeClaims;
        try
        {
            Check(NetCampaignEvidence.Enabled, "evidence environment is enabled before static initialization");
            long receivedBefore = Claims().GetProperty("Received").GetInt64();
            long appliedBefore = Claims().GetProperty("Applied").GetInt64();
            typeof(NetSession).GetProperty(nameof(NetSession.Role))!.SetValue(null, NetRole.Host);
            typeof(NetSession).GetProperty(nameof(NetSession.IsAuthority))!.SetValue(null, true);
            typeof(NetSession).GetProperty(nameof(NetSession.ServerMatch))!.SetValue(null,
                new MatchStatePacket { MatchId = 41, AuthorityEpoch = 7, RoomKey = "unit-no-assets" });
            NetHitClaims.Enabled = true; NetHitClaims.Reset();
            Check(NetHitClaims.Arbitrating, "actual authority receiver is enabled");
            var claim = new HitClaimPacket
            {
                MatchId = 41, AuthorityEpoch = 7, ShooterGeneration = 1, ShooterLifeId = 0,
                VictimGeneration = 1, VictimLifeId = 1, VictimSlot = 1, ClaimId = 19,
                ShotId = 1, ComponentId = 1, Frame = 10, AckFrame = 10, LaunchFrame = 10,
                Beam = (byte)BeamType.PowerBeam, Damage = 10, HitPoint = Vector3.Zero,
                Direction = Vector3.UnitZ, Flags = HitClaimPacket.FlagDirect
            };
            byte[] payload = new byte[1 + HitClaimPacket.Size]; payload[0] = 1;
            claim.Write(payload.AsSpan(1));
            var decoded = HitClaimPacket.Read(payload.AsSpan(1));
            Check(decoded.ClaimId == claim.ClaimId && decoded.ShooterLifeId == 0 && decoded.VictimLifeId == 1,
                "well-formed codec preserves the deliberate lifecycle-invalid claim");
            NetHitClaims.Receive(0, payload.AsSpan(0, payload.Length - 1));
            Check(NetHitClaims.Received == 0 && Claims().GetProperty("Received").GetInt64() == receivedBefore,
                "truncated entries are not counted as decoded receipts");
            NetHitClaims.Receive(0, payload);
            Check(NetHitClaims.Received == 1 && NetPlayerLifecycle.OldLifeClaims == previousOldLife + 1,
                "actual decoded invalid claim is received and rejected by lifecycle policy");
            Check(NetHitClaims.AppliedHere == 0 && Claims().GetProperty("Applied").GetInt64() == appliedBefore,
                "invalid lifecycle never creates a gameplay application");
            Check(Claims().GetProperty("Received").GetInt64() == receivedBefore + 1,
                "process evidence counts the real receiver entry");
            NetHitClaims.Reset();
            Check(NetHitClaims.Received == 0 && NetHitClaims.AppliedHere == 0,
                "ordinary teardown resets the session snapshot counters");
            Check(Claims().GetProperty("Received").GetInt64() == receivedBefore + 1
                && Claims().GetProperty("Applied").GetInt64() == appliedBefore,
                "campaign totals survive the first teardown reset");
            NetHitClaims.Receive(0, payload); // Receipt counting explicitly includes retries.
            Check(NetHitClaims.Received == 1 && NetHitClaims.AppliedHere == 0
                && Claims().GetProperty("Received").GetInt64() == receivedBefore + 2,
                "a real retried invalid entry counts as another receipt without paid gameplay");
            NetHitClaims.Reset();
            // Evidence-only helper: this does not perform or claim a UDP admission.
            typeof(NetSession).GetProperty(nameof(NetSession.NetFrame))!.SetValue(null, 173u);
            var baseline = new WorldBootstrapIdentity(new(41, 7, 3), 9, 2, 141);
            NetCampaignEvidence.Ready(6, baseline.SlotGeneration, false, true, false, 25, 15, 5,
                NetSession.NetFrame, baseline.AuthorityFrame, baseline.Revision);
            NetCampaignEvidence.Ready(7, 1, false, true, false, 25, 15, -1);
            typeof(NetSession).GetProperty(nameof(NetSession.Role))!.SetValue(null, NetRole.Offline);
            NetCampaignEvidence.Write("unit-invalid-claim");
            using var document = JsonDocument.Parse(File.ReadAllText(output));
            var campaign = document.RootElement.GetProperty("CampaignClaims");
            var snapshot = document.RootElement.GetProperty("HitClaims");
            var readiness = document.RootElement.GetProperty("Readiness");
            var ready = readiness[readiness.GetArrayLength() - 2];
            Check(ready.GetProperty("AuthorityEpoch").GetUInt64() == 7
                && ready.GetProperty("CurrentMatchId").GetUInt32() == 41
                && ready.GetProperty("AuthorityFrame").GetUInt32() == 173
                && ready.GetProperty("BaselineAuthorityFrame").GetUInt32() == 141
                && ready.GetProperty("BootstrapRevision").GetUInt32() == 9
                && ready.GetProperty("Generation").GetUInt16() == 2,
                "readiness writer distinguishes native receipt frame from exact bootstrap identity");
            Check(readiness[readiness.GetArrayLength() - 1].GetProperty("AuthorityFrame").ValueKind == JsonValueKind.Null
                && readiness[readiness.GetArrayLength() - 1].GetProperty("BootstrapRevision").ValueKind == JsonValueKind.Null,
                "legacy evidence callers leave unknown readiness boundaries explicitly null");
            Check(campaign.GetProperty("Received").GetInt64() == receivedBefore + 2
                && campaign.GetProperty("Applied").GetInt64() == appliedBefore
                && snapshot.GetProperty("Received").GetInt64() == 0 && snapshot.GetProperty("AppliedHere").GetInt64() == 0,
                "actual post-reset evidence writer persists lifetime totals alongside zero session counters");
            Check(campaign.GetProperty("CountingUnit").GetString()!.Contains("including retries"),
                "report identifies receipt counting units rather than unique or accepted gameplay claims");
        }
        finally
        {
            NetHitClaims.Reset(); NetHitClaims.Enabled = previousEnabled;
            NetPlayerLifecycle.OldLifeClaims = previousOldLife;
            typeof(NetSession).GetProperty(nameof(NetSession.NetFrame))!.SetValue(null, previousFrame);
            typeof(NetSession).GetProperty(nameof(NetSession.Role))!.SetValue(null, previousRole);
            typeof(NetSession).GetProperty(nameof(NetSession.IsAuthority))!.SetValue(null, previousAuthority);
            typeof(NetSession).GetProperty(nameof(NetSession.ServerMatch))!.SetValue(null, previousMatch);
        }
    }
}
