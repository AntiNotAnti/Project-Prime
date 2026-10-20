using System;
using System.Text.Json;
using MphRead.Formats;
using MphRead.Mods.Movement;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

// Exercise the serialized observational contract as consumed by evidence tools,
// alongside the strongly typed model/profile identity checks.
internal static class MovementValidationTests
{
    internal static int Run()
    {
        bool previous = NetMovementValidationShadow.Enabled;
        try
        {
            NetMovementValidationShadow.Enabled = true;
            HuntersAndForms(); ModelsAndProfiles(); OrderingAndGaps(); Lifecycle(); RoomBoundary(); Exemptions(); InvalidSamples(); Bounds(); Disabled();
            Console.WriteLine("PASS: movement shadow lifecycle, source time, exemptions and bounded observations");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { NetMovementValidationShadow.Reset(); NetMovementValidationShadow.Enabled = previous; }
    }
    private static void Check(bool value, string message) => NetArchitectureTests.Check(value, message);
    private static MovementValidationSample Sample(uint frame, float x = 0, Hunter hunter = Hunter.Samus,
        IntentButtons buttons = IntentButtons.MoveUp, MovementValidationExemption exemptions = 0)
        => new(1, 1, 0, 1, 1, hunter, frame, new Vector3(x, 0, 0), buttons, exemptions);
    private static JsonElement Report() => JsonSerializer.SerializeToElement(NetMovementValidationShadow.Capture(),
        new JsonSerializerOptions { IncludeFields = true });
    private static long Count(string name) => Sum(Report(), name);
    private static long Sum(JsonElement value, string name)
    {
        long result = 0;
        if (value.ValueKind == JsonValueKind.Object)
            foreach (var field in value.EnumerateObject())
                result += field.Name == name && field.Value.ValueKind == JsonValueKind.Number
                    ? field.Value.GetInt64() : Sum(field.Value, name);
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var row in value.EnumerateArray()) result += Sum(row, name);
        return result;
    }
    private static void HuntersAndForms()
    {
        for (int h = 0; h < 7; h++)
        {
            NetMovementValidationShadow.Reset();
            for (uint f = 100; f <= 120; f++) NetMovementValidationShadow.Observe(Sample(f, (f - 100) * .05f, (Hunter)h));
            Check(Count("Baselines") == 1 && Count("Evaluated") == 20 && Count("WouldRefuse") == 0,
                $"hunter {h} ordinary biped source-frame movement evaluated");
            NetMovementValidationShadow.Observe(Sample(121, 10000, (Hunter)h));
            Check(Count("WouldRefuse") > 0, $"hunter {h} gross biped delta is observable");
            NetMovementValidationShadow.Observe(Sample(122, 10000, (Hunter)h, IntentButtons.AltFormState,
                MovementValidationExemption.FormTransition));
            NetMovementValidationShadow.Observe(Sample(123, 10000.05f, (Hunter)h, IntentButtons.AltFormState));
            NetMovementValidationShadow.Observe(Sample(124, 10000.1f, (Hunter)h, IntentButtons.AltFormState));
            Check(Count("Unsupported") == 1 && Count("Exempt") == 2,
                $"hunter {h} stable alt explicitly unsupported after transition settling");
        }
    }
    private static void ModelsAndProfiles()
    {
        NetMovementValidationShadow.Reset();
        var native = Sample(100);
        NetMovementValidationShadow.Observe(native);
        NetMovementValidationShadow.Observe(native with { Frame = 101, Position = new(10000, 0, 0) });
        var momentum = Sample(1) with { MovementModel = MovementModel.Momentum,
            ProfileVersion = MomentumProfileV1.MomentumProfileVersion };
        NetMovementValidationShadow.Observe(momentum);
        Check(Count("Baselines") == 2 && Count("DuplicateOrOld") == 0,
            "model/profile context establishes a fresh source-time baseline");
        NetMovementValidationShadow.Observe(momentum with { Frame = 2, Position = new(10000, 0, 0) });
        var report = NetMovementValidationShadow.Capture();
        Check(report.Schema == 3 && report.HardRejections == 0 && report.Hunters.Length == 42,
            "versioned bounded report groups three models without enforcement");
        Check(report.Hunters[0].MovementModel == MovementModel.Native && report.Hunters[0].ProfileVersion == 0
            && report.Hunters[0].WouldRefuse == 1
            && report.Hunters[14].MovementModel == MovementModel.Momentum && report.Hunters[14].ProfileVersion == 1
            && report.Hunters[14].WouldRefuse == 1,
            "Native and Momentum counters never combine and retain Native index order");
        Check(report.Rows.Length == 2 && report.Rows[0].MovementModel == MovementModel.Native
            && report.Rows[0].ProfileVersion == 0 && report.Rows[1].MovementModel == MovementModel.Momentum
            && report.Rows[1].ProfileVersion == 1, "anomaly rows retain model/profile provenance");

        var values = Metadata.PlayerValues[0];
        float nativeLimit = 2 * Fixed.ToFloat(Math.Max(values.WalkSpeedCap, values.StrafeSpeedCap)) + .1f;
        float momentumLimit = Math.Max(nativeLimit, 2 * MomentumProfileV1.RetentionSpeedCap / 30 + .1f);
        Check(momentumLimit > nativeLimit, "Momentum retention envelope is broader than Native for calibration Hunter");
        float displacement = (nativeLimit + momentumLimit) / 2;
        foreach (bool selected in new[] { false, true })
        {
            NetMovementValidationShadow.Reset();
            var start = selected ? momentum : native;
            NetMovementValidationShadow.Observe(start);
            NetMovementValidationShadow.Observe(start with { Frame = start.Frame + 1, Position = new(displacement, 0, 0) });
            Check(Count("WouldRefuse") == (selected ? 0 : 1),
                "selected advisory envelope differs without changing Native constants");
        }
        NetMovementValidationShadow.Observe(momentum with { ProfileVersion = 2 });
        NetMovementValidationShadow.Observe(native with { ProfileVersion = 1 });
        NetMovementValidationShadow.Observe(native with { MovementModel = (MovementModel)255 });
        Check(Count("Invalid") == 3 && Count("HardRejections") == 0,
            "unknown profile/model evidence is diagnostic only and cannot enter grouping keys");
        NetMovementValidationShadow.Reset();
        var quake = Sample(1) with { MovementModel = MovementModel.Quake3, ProfileVersion = Quake3MovementProfileV2.ProfileVersion };
        NetMovementValidationShadow.Observe(quake);
        NetMovementValidationShadow.Observe(quake with { Frame = 2, Position = new(1000, 0, 0) });
        NetMovementValidationShadow.Observe(quake with { Frame = 3, Position = new(2000, 0, 0) });
        var uncapped = NetMovementValidationShadow.Capture();
        Check(uncapped.HardRejections == 0 && uncapped.Hunters[28].MovementModel == MovementModel.Quake3
            && uncapped.Hunters[28].ProfileVersion == 2 && uncapped.Hunters[28].Evaluated == 2
            && uncapped.Hunters[28].WouldRefuse == 0,
            "uncapped Quake III retains independent model/profile measurements without a capped-speed signal");
    }

    private static void OrderingAndGaps()
    {
        NetMovementValidationShadow.Reset();
        NetMovementValidationShadow.Observe(Sample(100));
        NetMovementValidationShadow.Observe(Sample(120, 1));
        Check(Count("WouldRefuse") == 0 && Count("Evaluated") == 1,
            "twenty source frames permit ordinary travel regardless of immediate receipt");
        NetMovementValidationShadow.Observe(Sample(120, 9999));
        NetMovementValidationShadow.Observe(Sample(119, -9999));
        NetMovementValidationShadow.Observe(Sample(121, 1.05f));
        Check(Count("DuplicateOrOld") == 2 && Count("WouldRefuse") == 0,
            "duplicates and reordered forged poses cannot advance baseline");
        NetMovementValidationShadow.Observe(Sample(10000, 9999));
        Check(Count("LongGaps") > 0 && Count("WouldRefuse") == 0, "long loss gap establishes baseline instead of teleport signal");
        NetMovementValidationShadow.Reset();
        NetMovementValidationShadow.Observe(Sample(uint.MaxValue - 1));
        NetMovementValidationShadow.Observe(Sample(0, .1f));
        NetMovementValidationShadow.Observe(Sample(1, .15f));
        Check(Count("Evaluated") == 2 && Count("DuplicateOrOld") == 0 && Count("WouldRefuse") == 0,
            "source frame wrap preserves elapsed-time ordering");
    }
    private static void Lifecycle()
    {
        foreach (string field in new[] { "epoch", "match", "generation", "life", "hunter" })
        {
            NetMovementValidationShadow.Reset(); var first = Sample(100); NetMovementValidationShadow.Observe(first);
            var next = Sample(1, 9999);
            next = field switch
            {
                "epoch" => next with { Epoch = 2 }, "match" => next with { Match = 2 },
                "generation" => next with { Generation = 2 }, "life" => next with { Life = 2 },
                _ => next with { Hunter = Hunter.Kanden }
            };
            NetMovementValidationShadow.Observe(next);
            Check(Count("Baselines") == 2 && Count("WouldRefuse") == 0 && Count("DuplicateOrOld") == 0,
                field + " replacement cannot compare against stale motion");
        }
    }
    private static void RoomBoundary()
    {
        NetMovementValidationShadow.Reset();
        NetMovementValidationShadow.Observe(Sample(100));
        NetMovementValidationShadow.Observe(Sample(101, .05f));
        NetPlayerBridge.NoteRoomChanged();
        NetMovementValidationShadow.Observe(Sample(102, 9999));
        Check(Count("Baselines") == 2 && Count("Evaluated") == 1 && Count("WouldRefuse") == 0,
            "actual room boundary retires pose history without discarding accumulated measurements");
    }
    private static void Exemptions()
    {
        foreach (var reason in new[] { MovementValidationExemption.Teleport, MovementValidationExemption.Platform,
            MovementValidationExemption.JumpPad, MovementValidationExemption.Impulse,
            MovementValidationExemption.FormTransition, MovementValidationExemption.Climbing, MovementValidationExemption.Collision, MovementValidationExemption.Prime })
        {
            NetMovementValidationShadow.Reset(); NetMovementValidationShadow.Observe(Sample(100));
            NetMovementValidationShadow.Observe(Sample(101, 9999, exemptions: reason));
            NetMovementValidationShadow.Observe(Sample(102, 9999.05f));
            Check(Count("Exempt") > 0 && Count("WouldRefuse") == 0,
                reason + " exempts displacement and rebases subsequent ordinary motion");
        }
    }
    private static void InvalidSamples()
    {
        NetMovementValidationShadow.Reset();
        var valid = Sample(100); NetMovementValidationShadow.Observe(valid);
        var invalid = new[]
        {
            valid with { Frame = 101, Position = new Vector3(float.NaN, 0, 0) },
            valid with { Frame = 101, Position = new Vector3(0, float.PositiveInfinity, 0) },
            valid with { Frame = 101, Position = new Vector3(0, 0, float.NegativeInfinity) },
            valid with { Frame = 101, Position = new Vector3(100000, 0, 0) },
            valid with { Slot = -1 }, valid with { Slot = 8 },
            valid with { Hunter = Hunter.Guardian }, valid with { Hunter = (Hunter)255 },
            valid with { Epoch = 0 }, valid with { Match = 0 },
            valid with { Generation = 0 }, valid with { Life = 0 }
        };
        foreach (var sample in invalid) NetMovementValidationShadow.Observe(sample);
        NetMovementValidationShadow.Observe(Sample(101, .05f));
        Check(Count("Invalid") == invalid.Length && Count("Baselines") == 1
            && Count("Evaluated") == 1 && Count("WouldRefuse") == 0,
            "invalid coordinates and identities are diagnostic-only and cannot poison history");
        Check(Count("HardRejections") == 0, "observer has zero hard rejections even for invalid input");
        NetMovementValidationShadow.ResetSlot(0);
        NetMovementValidationShadow.Observe(Sample(1, 9999));
        Check(Count("Baselines") == 2 && Count("WouldRefuse") == 0, "slot removal explicitly retires history");
        NetMovementValidationShadow.ResetSlot(-1); NetMovementValidationShadow.ResetSlot(8);
        NetMovementValidationShadow.ResetHistory();
        NetMovementValidationShadow.Observe(Sample(1, -9999));
        Check(Count("Baselines") == 3 && Count("Invalid") == invalid.Length && Count("WouldRefuse") == 0,
            "history reset preserves campaign aggregates while retiring all slot baselines");
        NetMovementValidationShadow.Reset();
        Check(Count("Invalid") == 0 && Count("Baselines") == 0 && Count("WouldRefuse") == 0,
            "full reset clears bounded evidence and counters");
    }
    private static void Bounds()
    {
        NetMovementValidationShadow.Reset(); NetMovementValidationShadow.Observe(Sample(100));
        for (uint f = 101; f < 1101; f++) NetMovementValidationShadow.Observe(Sample(f, (f & 1) == 0 ? 0 : 10000));
        var report = Report();
        // Adapt only these report property names if the capture contract differs.
        Check(report.GetProperty("Rows").GetArrayLength() <= 128 && report.GetProperty("Overflow").GetInt64() > 0,
            "anomaly evidence bounded to 128 with explicit overflow");
        Check(Count("WouldRefuse") >= 129, "bounded evidence does not truncate aggregate signal counters");
    }
    private static void Disabled()
    {
        NetMovementValidationShadow.Reset(); NetMovementValidationShadow.Enabled = false;
        var sample = Sample(100, 10000); var unchanged = sample;
        NetMovementValidationShadow.Observe(sample);
        Check(sample == unchanged && Count("Baselines") == 0 && Count("WouldRefuse") == 0,
            "disabled observer neither changes samples nor records state");
        NetMovementValidationShadow.Enabled = true; NetMovementValidationShadow.Observe(sample);
        Check(sample == unchanged && Count("Baselines") == 1, "enabled observer has no mutation channel");
    }
}
