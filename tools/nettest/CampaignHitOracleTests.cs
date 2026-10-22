using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

// Constructed frozen-source/geometry fixtures invoking actual native collision
// primitives, not proof of authenticated UDP, an actual source or health payment.
internal static class CampaignHitOracleTests
{
    internal static int RunScene(string profile, string room, string? output = null)
    {
        try
        {
            if (!NetCampaignEvidence.TimelineEnabled)
                throw new InvalidOperationException("Native oracle fixture requires actual campaign evidence and timeline opt-in.");
            int before = JsonSerializer.SerializeToElement(NetCampaignHitOracle.Capture()).GetProperty("Rows").GetArrayLength();
            // Reuse the existing genuine native source/history/Spawn/Process
            // fixture. Its resources, placements and health are declared setup;
            // none of its claims/verdicts are passed to this oracle.
            int native = TimedImperialistImpactTests.RunScene(profile, room);
            var report = JsonSerializer.SerializeToElement(NetCampaignHitOracle.Capture());
            // Keep unexercised/failing raw observations before judging positive
            // controls; a failure must not erase the useful diagnostic capture.
            if (output != null)
            {
                using var stream = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                JsonSerializer.Serialize(stream, report, new JsonSerializerOptions { WriteIndented = true });
            }
            if (native != 0) return native;
            var rows = report.GetProperty("Rows").EnumerateArray().Skip(before).ToArray();
            var contacts = rows.Where(r => r.GetProperty("Status").GetString() == "SourceQualifiedContact"
                && r.GetProperty("Source").GetString() == "AcceptedAuthoredSource"
                && r.GetProperty("World").GetString() == "HistoricalFirstSegment").ToArray();
            int misses = rows.Count(r => r.GetProperty("Status").GetString() == "SourceQualifiedMiss");
            if (!contacts.Any(r => r.GetProperty("HeadBand").GetBoolean())
                || !contacts.Any(r => !r.GetProperty("HeadBand").GetBoolean()) || misses == 0)
                throw new InvalidOperationException("Actual native oracle did not exercise historical accepted body/head and miss controls.");
            if (rows.Any(r => r.GetProperty("DamageEligibility").GetString() != "NOT_ASSESSED"
                || r.GetProperty("Payment").GetString() != "NOT_ASSESSED"))
                throw new InvalidOperationException("Geometry observation was incorrectly promoted to payment eligibility.");
            if (report.GetProperty("Overflow").GetInt64() != 0)
                throw new InvalidOperationException("Native oracle observation storage overflowed.");
            Console.WriteLine($"PASS: native accepted oracle geometry rows={rows.Length}, historical contacts={contacts.Length}, misses={misses}.");
            Console.WriteLine("SCOPE: declared-resource native internal-admission fixture; independent frozen collision replay, not authenticated UDP/natural pickup resources, legal damage, visible pixels or unique campaign-source denominator. Fixture cases may reuse full source keys after explicit reset.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    internal static int Run()
    {
        try
        {
            int checks = 0;
            void Check(bool value, string label)
            { if (!value) throw new InvalidOperationException("Campaign hit oracle: " + label); checks++; }
            var key = new ShotKey(1, 2, 0, 3, 4, uint.MaxValue);
            var ray = new CampaignOracleRay(key, 1, (byte)BeamType.Imperialist, 100, 80, 0, 81,
                CampaignOracleWorld.HistoricalFirstSegment, CampaignOracleSource.AcceptedAuthoredSource,
                ShotProofReason.Verified, new(0, 1, 0), new(0, 1, 20), .1f, 1000, true, true, true);
            var body = new CampaignOracleBody(new HistoricalBody(1, new(0, 0, 5), .5f, 0, 2), 5, 6, 81);
            var clear = new CampaignOracleObstacle(true, false, 0);
            var bodies = new[] { body };
            var before = bodies.ToArray();
            var hit = NetCampaignHitOracle.Evaluate(ray, bodies, clear);
            Check(hit.Status == CampaignOracleStatus.SourceQualifiedContact && hit.Victim == 1
                && hit.VictimGeneration == 5 && hit.VictimLife == 6 && !hit.HeadBand,
                "independent ray/body intersection yields exact lifecycle contact");
            Check(bodies.SequenceEqual(before) && ray.Key == key, "evaluation leaves frozen inputs unchanged");
            var miss = NetCampaignHitOracle.Evaluate(ray with { End = new(20, 1, 0) }, bodies, clear);
            Check(miss.Status == CampaignOracleStatus.SourceQualifiedMiss, "offered miss remains an observed miss without claim or payment");
            var wall = NetCampaignHitOracle.Evaluate(ray, bodies, new(true, true, .1f));
            Check(wall.Status == CampaignOracleStatus.SourceQualifiedMiss && wall.Reason == CampaignOracleReason.Obstructed,
                "native obstacle before body prevents expected contact");
            Check(NetCampaignHitOracle.Evaluate(ray, bodies, new(true, true, .9f)).Victim == 1,
                "wall behind body does not hide nearer native contact");
            var head = NetCampaignHitOracle.Evaluate(ray with { Start = new(0, 1.9f, 0), End = new(0, 1.9f, 20) }, bodies, clear);
            Check(head.Status == CampaignOracleStatus.SourceQualifiedContact && head.HeadBand,
                "native biped head band uses collision height rather than claimed point");
            var tangentIn = body with { Body = body.Body with { Position = new(.599f, 0, 5) } };
            var tangentOut = body with { Body = body.Body with { Position = new(.601f, 0, 5) } };
            Check(NetCampaignHitOracle.Evaluate(ray, new[] { tangentIn }, clear).Status == CampaignOracleStatus.SourceQualifiedContact
                && NetCampaignHitOracle.Evaluate(ray, new[] { tangentOut }, clear).Status == CampaignOracleStatus.SourceQualifiedMiss,
                "near tangent native cylinder radius separates hit/miss without proximity tolerance");
            var closer = body with { Body = body.Body with { Slot = 2, Position = new(0, 0, 3) }, Life = 7 };
            Check(NetCampaignHitOracle.Evaluate(ray, new[] { body, closer }, clear).Victim == 2,
                "full competitor inventory selects earlier contact");
            var tied = body with { Body = body.Body with { Slot = 2 } };
            Check(NetCampaignHitOracle.Evaluate(ray, new[] { body, tied }, clear).Reason == CampaignOracleReason.AmbiguousContact,
                "equal detached body intersections refuse ambiguous attribution");
            foreach (var ambiguous in new[] { body with { Life = 0 }, body with { Generation = 0 }, body with { WorldFrame = 82 } })
                Check(NetCampaignHitOracle.Evaluate(ray, new[] { ambiguous }, clear).Status == CampaignOracleStatus.Unavailable,
                    "missing or mismatched frozen lifecycle/frame stays unavailable");
            Check(NetCampaignHitOracle.Evaluate(ray, new[] { body, body with { Life = 7 } }, clear).Reason == CampaignOracleReason.LifecycleAmbiguous,
                "cross-life duplicate slot cannot merge into one target");
            foreach (var unsupported in new[] { ray with { FirstSegment = false }, ray with { SupportedFamily = false },
                ray with { Weapon = (byte)BeamType.Missile }, ray with { Component = 2 } })
                Check(NetCampaignHitOracle.Evaluate(unsupported, bodies, clear).Reason == CampaignOracleReason.UnsupportedFamily,
                    "unsupported later/child/projectile family never borrows straight-ray proof");
            foreach (var unavailable in new[] { ray with { CompleteWorld = false }, ray with { World = CampaignOracleWorld.Unavailable } })
                Check(NetCampaignHitOracle.Evaluate(unavailable, bodies, clear).Reason == CampaignOracleReason.WorldUnavailable,
                    "incomplete history or world cannot prove a hit or miss");
            Check(NetCampaignHitOracle.Evaluate(ray, bodies, default).Status == CampaignOracleStatus.Unavailable,
                "missing obstacle query is not known clear LOS");
            Check(NetCampaignHitOracle.Evaluate(ray with { Source = CampaignOracleSource.UnqualifiedNativeCopy }, bodies, clear)
                .Reason == CampaignOracleReason.SourceUnqualified,
                "offered native copy is distinct from authenticated/source-qualified contact");
            Check(NetCampaignHitOracle.Evaluate(ray with { SourceReason = ShotProofReason.SourceUnavailable }, bodies, clear)
                .Status == CampaignOracleStatus.Unavailable, "source admission alone does not certify resources/pose");
            Check(NetCampaignHitOracle.Evaluate(ray with { Source = (CampaignOracleSource)99 }, bodies, clear)
                .Reason == CampaignOracleReason.SourceUnqualified
                && NetCampaignHitOracle.Evaluate(ray with { World = (CampaignOracleWorld)99 }, bodies, clear)
                .Reason == CampaignOracleReason.WorldUnavailable, "unknown provenance/world enum cannot certify a source or history");
            Check(NetCampaignHitOracle.Evaluate(ray with { MaximumDistance = 4 }, bodies, clear)
                .Reason == CampaignOracleReason.UnsupportedFamily,
                "range termination before native segment end is an unsupported competing collision");
            Check(NetCampaignHitOracle.Evaluate(ray with { World = CampaignOracleWorld.CurrentNativeSegment }, bodies, clear).Status
                == CampaignOracleStatus.SourceQualifiedContact, "explicit current geometry remains distinct from historical ray metadata");
            Check(NetCampaignHitOracle.Evaluate(ray, new[] { body with { Body = body.Body with { Type = HistoricalBodyType.AltSphere } } }, clear)
                .Reason == CampaignOracleReason.UnsupportedBody, "unimplemented alternate body remains unavailable");
            foreach (var invalid in new[] { ray with { Start = new(float.NaN, 0, 0) }, ray with { Radius = -1 },
                ray with { End = ray.Start } })
                Check(NetCampaignHitOracle.Evaluate(invalid, bodies, clear).Reason == CampaignOracleReason.InvalidGeometry,
                    "invalid finite segment does not yield a denominator");
            var buffer = new CampaignOracleBuffer(1);
            buffer.Add(ray, clear, bodies, hit); bodies[0] = closer;
            var captured = buffer.Capture();
            Check(captured[0].Bodies[0] == body && captured[0].Result == hit, "retained pose/source/result are detached values");
            captured[0].Bodies[0] = closer;
            Check(buffer.Capture()[0].Bodies[0] == body, "report mutation cannot change retained geometry");
            buffer.Add(ray, clear, bodies, miss);
            Check(buffer.Overflow == 1 && buffer.Capture().Length == 1, "overflow is explicit bounded incomplete evidence");
            Check(typeof(CampaignOracleRay).GetProperties().All(p => !p.Name.Contains("Claim") && !p.Name.Contains("Payment")),
                "oracle input has no claim point/verdict/payment dependency");
            Console.WriteLine($"PASS: {checks} constructed frozen native-contact oracle assertions; no UDP, damage eligibility, payment or pixels certified.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
