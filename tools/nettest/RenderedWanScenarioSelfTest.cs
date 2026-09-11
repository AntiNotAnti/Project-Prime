using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using ProjectPrime.Server.Shared;

namespace MphRead.NetTest;

internal static partial class RenderedWanValidationCheck
{
    /// <summary>
    /// Exercises the scenario bookkeeping without starting SDL, Node, or a
    /// Worker. These checks deliberately use observed target facts rather than
    /// accepting the schedule labels as evidence.
    /// </summary>
    public static int ScenarioSelfTest()
    {
        int cases = 0;
        Require(RenderedWanScenarioParser.Parse("general") == RenderedWanScenario.General);
        Require(RenderedWanScenarioParser.Parse("headshot") == RenderedWanScenario.Headshot);
        Require(RenderedWanScenarioParser.Format(RenderedWanScenario.Headshot) == "headshot");
        ExpectScenarioFailure(() => RenderedWanScenarioParser.Parse("HEADSHOT"));
        cases++;

        Guid oldAdmission = Guid.NewGuid();
        Guid freshAdmission = Guid.NewGuid();
        byte[] oldKey = Enumerable.Range(1, AdmissionKeyRules.ByteLength).Select(value => (byte)value).ToArray();
        byte[] freshKey = Enumerable.Range(33, AdmissionKeyRules.ByteLength).Select(value => (byte)value).ToArray();
        using (var transport = new NetTransport(0))
        using (var client = new NetClient(transport, new IPEndPoint(IPAddress.Loopback, 1),
                   "RECONNECT-REGRESSION", Hunter.Noxus, nonce: 1, ticket: "old.ticket.sig",
                   wireMatchId: 7, admissionId: oldAdmission, authKey: oldKey,
                   udpAuthenticationEnabled: true))
        {
            NodeMatchHandoff freshHandoff = new(Guid.NewGuid(), 7, "127.0.0.1", 1,
                "fresh.ticket.sig", 2, false, Hunter.Noxus, freshAdmission,
                Convert.ToBase64String(freshKey), true);
            ApplyReconnectHandoff(client, freshHandoff);
            Require(client.AdmissionId == freshAdmission && client.State == NetConnectionState.Connecting);

            NodeMatchHandoff reusedHandoff = freshHandoff with
            {
                Ticket = "another.ticket.sig",
                Nonce = 3
            };
            ExpectScenarioFailure(() => ApplyReconnectHandoff(client, reusedHandoff));
        }
        CryptographicOperations.ZeroMemory(oldKey);
        CryptographicOperations.ZeroMemory(freshKey);
        cases++;

        Require(HeadshotScenarioStage.At(0, 12)
            == new HeadshotScenarioStage(HeadshotScenarioVariant.Vertical, HeadshotScenarioRange.Close));
        Require(HeadshotScenarioStage.At(180, 12)
            == new HeadshotScenarioStage(HeadshotScenarioVariant.Vertical, HeadshotScenarioRange.Long));
        Require(HeadshotScenarioStage.At(360, 12)
            == new HeadshotScenarioStage(HeadshotScenarioVariant.Strafe, HeadshotScenarioRange.Close));
        Require(HeadshotScenarioStage.At(540, 12)
            == new HeadshotScenarioStage(HeadshotScenarioVariant.Strafe, HeadshotScenarioRange.Long));
        HeadshotValidationPlan plan0 = HeadshotValidationController.Plan(0, 12, 6);
        HeadshotValidationPlan plan1 = HeadshotValidationController.Plan(180, 12, 20);
        HeadshotValidationPlan plan2 = HeadshotValidationController.Plan(360, 12, 6);
        HeadshotValidationPlan plan3 = HeadshotValidationController.Plan(540, 12, 20);
        Require(plan0.Arm == 0 && plan0.Vertical && !plan0.LongRange
            && plan1.Arm == 1 && plan1.Vertical && plan1.LongRange
            && plan2.Arm == 2 && !plan2.Vertical && !plan2.LongRange
            && plan3.Arm == 3 && !plan3.Vertical && plan3.LongRange);
        HeadshotValidationPlan calibration = HeadshotValidationController.Plan(
            HeadshotValidationController.StationaryCalibrationFrames - 1, 12, 6);
        HeadshotValidationPlan firstMotion = HeadshotValidationController.Plan(
            HeadshotValidationController.StationaryCalibrationFrames, 12, 6);
        Require(HeadshotValidationController.StationaryCalibrationFrames
            == HeadshotScenarioThresholds.StationaryCalibrationFrames);
        Require(calibration.Buttons == InputButtons.None
            && calibration.Pressed == InputButtons.None
            && (firstMotion.Buttons & InputButtons.Jump) != 0
            && (firstMotion.Pressed & InputButtons.Jump) != 0);
        InputCommand controlled = HeadshotValidationController.CreateCommand(
            HeadshotValidationController.StationaryCalibrationFrames,
            12, 6, Vector3.UnitX, inputEpoch: 7);
        CombatActor controlledActor = new(1, 22, controlled.InputEpoch);
        Require(controlledActor.IsValid && controlled.InputEpoch == 7
            && (controlled.Buttons & InputButtons.Jump) != 0
            && (controlled.Pressed & InputButtons.Jump) != 0);
        InputCommand respawn = HeadshotValidationController.CreateRespawnCommand(
            91, Vector3.UnitX, inputEpoch: 7);
        Require(respawn.InputEpoch == 7
            && respawn.Buttons == InputButtons.Shoot
            && respawn.Pressed == InputButtons.None);
        Require(HeadshotValidationController.NeedsRespawn(0)
            && !HeadshotValidationController.NeedsRespawn(1));
        Require(HeadshotScenarioAim.IsBodyCalibration(
                targetLife: 2, trackedLife: 2, lifeStartFrame: 120,
                lifeKnown: true, scenarioTick: 120)
            && HeadshotScenarioAim.IsBodyCalibration(
                targetLife: 2, trackedLife: 2, lifeStartFrame: 120,
                lifeKnown: true, scenarioTick: 209)
            && !HeadshotScenarioAim.IsBodyCalibration(
                targetLife: 2, trackedLife: 2, lifeStartFrame: 120,
                lifeKnown: true, scenarioTick: 210)
            && !HeadshotScenarioAim.IsBodyCalibration(
                targetLife: 3, trackedLife: 2, lifeStartFrame: 120,
                lifeKnown: true, scenarioTick: 150));
        cases++;

        var facts = new HeadshotScenarioFacts();
        SnapshotPlayer shooter = new()
        {
            Slot = 0, ConnectionId = 11, Life = 1, Health = 100,
            Position = Vector3.Zero, Aim = Vector3.UnitX,
            Flags = SnapshotPlayerFlags.Active | SnapshotPlayerFlags.Spawned
        };
        CombatActor actor = new(0, 11, 1);
        CombatActor targetActor = new(1, 22, 1);
        for (uint frame = 0; frame < 720; frame++)
        {
            HeadshotScenarioStage stage = HeadshotScenarioStage.At(frame, 12);
            bool longRange = stage.Range == HeadshotScenarioRange.Long;
            bool strafe = stage.Variant == HeadshotScenarioVariant.Strafe;
            float range = longRange ? 20f : 6f;
            float lateral = strafe ? (frame % 20) * 0.02f : 0;
            SnapshotPlayer target = new()
            {
                Slot = 1, ConnectionId = 22, Life = 1, Health = 100,
                Hunter = Hunter.Noxus,
                Position = new Vector3(range + lateral, strafe ? 0 : frame * 0.01f, 0),
                Speed = strafe ? new Vector3(0.25f, 0, 0) : new Vector3(0, 1, 0),
                Flags = SnapshotPlayerFlags.Active | SnapshotPlayerFlags.Spawned
                    | (strafe ? SnapshotPlayerFlags.Grounded : SnapshotPlayerFlags.None)
            };
            if (frame < 60)
            {
                target.Position = new Vector3(6, 0, 0);
                target.Speed = Vector3.Zero;
                target.Flags = SnapshotPlayerFlags.Active | SnapshotPlayerFlags.Spawned
                    | SnapshotPlayerFlags.Grounded;
            }
            SnapshotPlayer aimedShooter = shooter;
            Vector3 head = HeadshotScenarioGeometry.TryGetHeadPoint(target,
                target.Position, out Vector3 point) ? point : target.Position;
            Vector3 aimPoint = frame < 30 ? target.Position : head;
            aimedShooter.Aim = (aimPoint - aimedShooter.Position).Normalized();
            facts.ObserveTarget(target, aimedShooter, stage,
                bodyCalibrationAim: frame < 30);
        }
        Require(facts.FramesObserved == 720 && facts.FramesOnTarget == 690);
        Require(facts.VerticalFrames >= 30 && facts.StrafeFrames >= 30);
        Require(facts.CloseFrames >= 30 && facts.LongFrames >= 30);
        Require(facts.TargetAirborneFrames >= 30 && facts.TargetMovedFrames >= 30);
        Require(facts.MaximumVerticalSpeed >= 1);

        // A rendered client can spend the first calibration frames presenting
        // the target before the aim edge arrives. Stationary samples must be
        // tied to the observed pose, not discarded solely because they occur
        // after the first 60 local observations.
        var delayedAimFacts = new HeadshotScenarioFacts();
        for (uint frame = 0; frame < 120; frame++)
        {
            bool settling = frame < 20;
            SnapshotPlayer delayedTarget = new()
            {
                Slot = 1, ConnectionId = 22, Life = 1,
                Hunter = Hunter.Noxus,
                Position = new Vector3(6, settling ? frame * 0.2f : 4f, 0),
                Speed = settling ? Vector3.UnitY : Vector3.Zero,
                Flags = SnapshotPlayerFlags.Active | SnapshotPlayerFlags.Spawned
                    | (settling ? SnapshotPlayerFlags.None : SnapshotPlayerFlags.Grounded)
            };
            Vector3 delayedHead = HeadshotScenarioGeometry.TryGetHeadPoint(
                delayedTarget, delayedTarget.Position, out Vector3 delayedPoint)
                ? delayedPoint : delayedTarget.Position;
            Vector3 delayedAimPoint = frame < 60 ? -Vector3.UnitZ
                : frame < HeadshotScenarioThresholds.StationaryBodyAimFrames
                    ? delayedTarget.Position : delayedHead;
            SnapshotPlayer delayedShooter = shooter;
            delayedShooter.Aim = (delayedAimPoint - delayedShooter.Position).Normalized();
            bool bodyCalibrationAim = frame >= 60
                && frame < HeadshotScenarioThresholds.StationaryBodyAimFrames;
            delayedAimFacts.ObserveTarget(delayedTarget, delayedShooter,
                HeadshotScenarioStage.At(frame, 12), bodyCalibrationAim);
        }
        Require(delayedAimFacts.StationaryBodyFrames >= 20
            && delayedAimFacts.StationaryHeadFrames >= 20);

        // At long range the body/head direction cones overlap. A body-phase
        // frame must remain body-only even when it also falls inside the head
        // cone, otherwise one pose could satisfy both calibration gates.
        var overlappingConeFacts = new HeadshotScenarioFacts();
        SnapshotPlayer distantTarget = new()
        {
            Slot = 1, ConnectionId = 22, Life = 1,
            Hunter = Hunter.Noxus, Position = new Vector3(20, 0, 0),
            Speed = Vector3.Zero,
            Flags = SnapshotPlayerFlags.Active | SnapshotPlayerFlags.Spawned
                | SnapshotPlayerFlags.Grounded
        };
        SnapshotPlayer distantShooter = shooter;
        distantShooter.Aim = (distantTarget.Position - distantShooter.Position).Normalized();
        for (int frame = 0; frame < 30; frame++)
            overlappingConeFacts.ObserveTarget(distantTarget, distantShooter,
                HeadshotScenarioStage.At((uint)frame, 12), bodyCalibrationAim: true);
        Require(overlappingConeFacts.StationaryBodyFrames >= 20
            && overlappingConeFacts.StationaryHeadFrames == 0);
        cases++;

        for (uint sequence = 0; sequence < 36; sequence++)
        {
            facts.Trigger();
            facts.Shots.ObserveLocalRoot(actor, sequence, (byte)BeamType.Imperialist);
            facts.Shots.ObserveAuthoritativeRoot(new CombatEvent(
                sequence + 1, sequence, sequence, CombatEventKind.Shot,
                (byte)BeamType.Imperialist, CombatEventFlags.None, actor,
                CombatActor.None, 100, 0, Vector3.Zero, Vector3.UnitX, 0, 0, 0));
            facts.Shots.ObservePredictedContact(new CombatShot(actor, sequence,
                sequence, sequence, sequence, 0)
            {
                SourceWeapon = (byte)BeamType.Imperialist
            }, targetActor, headshot: true);
            facts.Shots.ObserveAuthoritativeDamage(new CombatEvent(
                sequence + 100, sequence, sequence, CombatEventKind.Damage,
                (byte)BeamType.Imperialist, CombatEventFlags.Headshot, actor,
                targetActor, 90, 10, Vector3.Zero, Vector3.UnitX, 0, 0, 0));
        }
        Require(facts.Shots.LocalRootShots == 36);
        Require(facts.Shots.AuthoritativeRootShots == 36);
        Require(facts.Shots.CorrelatedRootShots == 36);
        Require(facts.Shots.CountLocalWeapon((byte)BeamType.Imperialist) == 36);
        Require(facts.Shots.CountAuthorityWeapon((byte)BeamType.Imperialist) == 36);
        Require(facts.Shots.PredictedContacts == 36 && facts.Shots.PredictedHeadshots == 36);
        Require(facts.Shots.AuthoritativeHits == 36 && facts.Shots.AuthoritativeHeadshots == 36);
        Require(facts.Shots.ConfirmedHeadshots == 36
            && facts.Shots.DowngradedHeadshots == 0
            && facts.Shots.PromotedHeadshots == 0
            && facts.Shots.DeniedHeadshots == 0);
        Require(facts.StationaryBodyFrames >= 20 && facts.StationaryHeadFrames >= 20);
        cases++;

        ScenarioGateResult valid = ScenarioGateResult.ValidateHeadshot(
            facts, correctWeapon: true, authorityWeapon: true, zoomed: true,
            ammoAvailable: true, minimumDuration: true, validParticipant: true,
            authoritativeWorker: true, deterministicTarget: true, cleanLink: true);
        Require(valid.Valid && valid.ChoreographyValid && valid.ShotCorrelationValid
            && valid.CombatCoverageValid && valid.HeadshotEvidenceValid
            && valid.FailureReasons.Length == 0);
        ScenarioGateResult invalid = ScenarioGateResult.ValidateHeadshot(
            facts, correctWeapon: false, authorityWeapon: true, zoomed: true,
            ammoAvailable: true, minimumDuration: true, validParticipant: true,
            authoritativeWorker: true, deterministicTarget: false, cleanLink: true);
        Require(!invalid.Valid && !invalid.ShotCorrelationValid
            && !invalid.ChoreographyValid
            && invalid.FailureReasons.Contains("weapon-not-imperialist")
            && invalid.FailureReasons.Contains("target-trajectory-not-deterministic"));
        cases++;

        string root = Path.Combine(Path.GetTempPath(),
            "prime-rendered-wan-scenario-selftest-" + Guid.NewGuid().ToString("N"));
        try
        {
            string reserved = RenderedWanRunReservation.ReserveNewDirectory(root);
            Require(Path.GetFullPath(reserved) == Path.GetFullPath(root)
                && File.Exists(Path.Combine(root, ".run-reservation")));
            ExpectScenarioFailure(() => RenderedWanRunReservation.ReserveNewDirectory(root));
            cases++;
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }

        Console.WriteLine($"RENDERED_WAN_SCENARIO_SELF_TEST result=PASS cases={cases}");
        return 0;
    }

    private static void Require(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Rendered WAN scenario self-test assertion failed.");
    }

    private static void ExpectScenarioFailure(Action action)
    {
        try
        {
            action();
        }
        catch
        {
            return;
        }
        throw new InvalidOperationException("Rendered WAN scenario self-test expected a failure.");
    }
}
