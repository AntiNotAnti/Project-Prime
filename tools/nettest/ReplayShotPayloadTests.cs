using System;
using System.Collections.Generic;
using MphRead;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

/// <summary>
/// Exercises the production ReplayRecorder, not a copy of its packet writer.
/// The wire's optional FireEvent/Ray/Target fields must survive the complete
/// canonical intent record even when the carrier arrives later than movement.
/// </summary>
internal static class ReplayShotPayloadTests
{
    internal static int Run()
    {
        int count = 0;
        void Check(bool ok, string name)
        {
            count++;
            if (!ok) throw new InvalidOperationException("REPLAY SHOT PAYLOAD " + name);
        }
        var match = new MatchStatePacket
        {
            MatchId = 7, AuthorityEpoch = 9, RoomKey = "MP1 SANCTORUS",
            Mode = (byte)GameMode.Battle
        };
        var recorder = new ReplayRecorder();
        recorder.AcceptMatch(match, 0);
        var records = new List<byte[]>();
        recorder.Accepted += record =>
        {
            if (record.Kind == ReplayFactKind.Intent)
                records.Add(record.Payload.ToArray());
        };
        BeamType[] weapons =
        {
            BeamType.Missile, BeamType.Imperialist, BeamType.Magmaul,
            BeamType.VoltDriver, BeamType.Battlehammer, BeamType.Judicator,
            BeamType.ShockCoil, BeamType.PowerBeam
        };
        try
        {
            for (int i = 0; i < weapons.Length; i++)
            {
                var fire = new FireEvent((uint)(100 + i), (uint)(200 + i),
                    (uint)(180 + i), 64, FireEventKind.PressFire,
                    (byte)weapons[i], (byte)(i + 2), 0,
                    (byte)(FireEvent.FlagPose | FireEvent.FlagReticle),
                    new Vector3(1 + i, 2, 3),
                    Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitY,
                    new Vector2(.25f, .75f), SourceLife: 1,
                    Target: NetTargetIdentity.None);
                var intent = new IntentPacket
                {
                    MatchId = 7, AuthorityEpoch = 9, SlotGeneration = 2,
                    LifeId = 1, Frame = (uint)(200 + i),
                    AckFrame = fire.AckFrame,
                    HasFireEvents = true, FireEventCount = 1
                };
                intent.FireEvents[0] = fire;
                recorder.AcceptIntent(i % 2, intent, (uint)(300 + i));
                byte[] saved = records[^1];
                Check(saved.Length == 2 + IntentPacket.TouchCanonicalSize,
                    "canonical full intent size for " + weapons[i]);
                Check(saved[0] == (byte)PacketType.SlotIntent
                    && saved[1] == i % 2, "recorded source slot " + weapons[i]);
                var decoded = IntentPacket.Read(saved.AsSpan(2));
                Check(decoded.HasFireEvents && decoded.FireEventCount == 1
                    && decoded.FireEvents[0].ShotId == fire.ShotId
                    && decoded.FireEvents[0].Weapon == fire.Weapon
                    && decoded.FireEvents[0].AckFrame == fire.AckFrame
                    && decoded.FireEvents[0].Charge == fire.Charge,
                    "shot identity and weapon preserved " + weapons[i]);
                Check(decoded.FireEvents[0].HasPose && decoded.FireEvents[0].HasReticle
                    && decoded.FireEvents[0].Origin == fire.Origin
                    && decoded.FireEvents[0].Direction == fire.Direction
                    // Reticle coordinates are intentionally quantized to
                    // ushort on the canonical wire (1/65535 resolution).
                    // Compare within that defined wire precision instead
                    // of exact binary-float equality.
                    && MathF.Abs(decoded.FireEvents[0].Reticle.X - fire.Reticle.X) <= 1f / ushort.MaxValue
                    && MathF.Abs(decoded.FireEvents[0].Reticle.Y - fire.Reticle.Y) <= 1f / ushort.MaxValue,
                    "authored ray, origin and reticle retained " + weapons[i]);
            }
            Check(records.Count == weapons.Length, "every owner and remote carrier recorded");

            // Idle intents remain readable and cannot fabricate an event.
            var quiet = new IntentPacket
            {
                MatchId = 7, AuthorityEpoch = 9, SlotGeneration = 2,
                LifeId = 1, Frame = 500
            };
            recorder.AcceptIntent(0, quiet, 500);
            // HasFireEvents denotes that the canonical payload carries the
            // fire-event section, not that a shot actually occurred. The
            // zero count is the invariant preventing invented idle shots.
            Check(records.Count == weapons.Length + 1
                && IntentPacket.Read(records[^1].AsSpan(2)).HasFireEvents
                && IntentPacket.Read(records[^1].AsSpan(2)).FireEventCount == 0,
                "idle canonical carrier retains an empty fire-event section");

            // The existing compact-intent suite exercises older event
            // carriers, replica acceptance and held movement bootstraps.
            Check(CompactIntentReplayTests.Run() == 0,
                "historical compact intent replay compatibility");
            Check(BombProtocolTests.Run() == 0, "bomb packet protocol compatibility");

            Console.WriteLine("REPLAY SHOT PAYLOAD PASS " + count + " checks");
            return 0;
        }
        finally
        {
            recorder.Reset();
        }
    }
}
