using System;
using System.Buffers.Binary;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using MphRead.Mods.Network;
using MphRead.Entities;
using OpenTK.Mathematics;

namespace MphRead.NetTest;
internal static class CompactIntentReplayTests
{
    private const BindingFlags All = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private delegate void ReplicaAccept(ReadOnlySpan<byte> packet, uint frame);
    private static void Check(bool ok, string label) => NetArchitectureTests.Check(ok, label);
    public static int Run()
    {
        try
        {
            var assembly = typeof(IntentPacket).Assembly;
            Type recorderType = assembly.GetType("MphRead.Mods.Network.ReplayRecorder", true)!;
            object recorder = Activator.CreateInstance(recorderType, true)!;
            recorderType.GetMethod("AcceptMatch", All)!.Invoke(recorder, new object[] {
                new MatchStatePacket { MatchId = 7, AuthorityEpoch = 9, RoomKey = "MP1 SANCTORUS", NextRoomKey = "", Mode = (byte)GameMode.Battle }, 0u });
            var newer = new IntentPacket { MatchId = 7, AuthorityEpoch = 9, SlotGeneration = 2,
                LifeId = 1, Frame = 102, Position = new(10, 0, 0), HasFireEvents = true, FireEventCount = 1 };
            newer.FireEvents[0] = new(43, 102, 80, 32, FireEventKind.PressFire, 0, 5, 0);
            var older = newer; older.Frame = 100; older.Position = new(2, 0, 0); older.FireEventCount = 2;
            older.FireEvents[0] = new(41, 100, 79, 128, FireEventKind.ReleaseFire, 0, 7, 0,
                FireEvent.FlagPose | FireEvent.FlagReticle, new(3, 4, 5), Vector3.UnitZ,
                Vector3.UnitX, Vector3.UnitY, new(.25f, .75f), SourceLife: 1);
            older.FireEvents[1] = older.FireEvents[0] with { ShotId = 42, Kind = FireEventKind.AutomaticFire };
            // Publication precedes the first restore point; transient payloads are
            // released immediately after the event, so copy them inside the callback.
            var published = new List<byte[]>();
            Action<ReplayTimelineRecord> onAccepted = record =>
            {
                if (record.Kind == ReplayFactKind.Intent) published.Add(record.Payload.ToArray());
            };
            recorderType.GetEvent("Accepted", All)!.GetAddMethod(true)!.Invoke(recorder, new object[] { onAccepted });
            var accept = recorderType.GetMethod("AcceptIntent", All)!;
            accept.Invoke(recorder, new object[] { 0, newer, 102u });
            accept.Invoke(recorder, new object[] { 0, older, 104u });
            Check(published.Count == 2 && published[1].Length == 2 + IntentPacket.TouchCanonicalSize
                && IntentPacket.Read(published[1].AsSpan(2)).Frame == 100
                && IntentPacket.Read(published[1].AsSpan(2)).FireEventCount == 2,
                "older carrier with unique shots is published as a canonical replay record");
            var bootstrap = (Array)recorderType.GetField("_intents", All)!.GetValue(recorder)!;
            object latest = bootstrap.GetValue(0)!;
            var payload = ((ReplayTimelineRecord)latest).Payload;
            Check(payload.Length == 2 + IntentPacket.TouchCanonicalSize && IntentPacket.Read(payload[2..]).Frame == 102
                && IntentPacket.Read(payload[2..]).Position.X == 10,
                "older carrier never replaces newest canonical bootstrap movement");
            Span<byte> live = stackalloc byte[IntentPacket.FullSize];
            int size = CompactIntentCodec.Write(older, live);
            Check(CompactIntentCodec.TryRead(live[..size], out var decoded), "live carrier decodes for canonical adapter");
            Span<byte> historical = stackalloc byte[IntentPacket.FullSize];
            Span<byte> adapted = stackalloc byte[IntentPacket.FullSize];
            older.Write(historical); decoded.Write(adapted);
            Check(historical.SequenceEqual(adapted) && IntentPacket.FullSize == 1287,
                "protocol 41 canonical bytes survive compact live adapter unchanged");

            Type replicaType = assembly.GetType("MphRead.Mods.Network.ReplayReplicaState", true)!;
            object replica = Activator.CreateInstance(replicaType, true)!;
            var replicaAccept = replicaType.GetMethod("Accept", All)!.CreateDelegate<ReplicaAccept>(replica);
            byte[] matchBytes = new byte[1 + MatchStatePacket.Size]; matchBytes[0] = (byte)PacketType.MatchState;
            new MatchStatePacket { MatchId = 7, AuthorityEpoch = 9, RoomKey = "MP1 SANCTORUS",
                NextRoomKey = "", Mode = (byte)GameMode.Battle }.Write(matchBytes.AsSpan(1));
            replicaAccept(matchBytes, 0);
            var roster = RosterPacket.Create(); roster.MatchId = 7; roster.AuthorityEpoch = 9; roster.Count = 1;
            roster.Slots[0] = 0; roster.Generations[0] = 2; roster.Names[0] = "actor";
            byte[] rosterBytes = new byte[1 + RosterPacket.Size]; rosterBytes[0] = (byte)PacketType.Roster;
            roster.Write(rosterBytes.AsSpan(1)); replicaAccept(rosterBytes, 0);
            int healthOffset = 1 + SnapshotHeader.Size + PlayerState.Size + NetMatchTimeSync.Size;
            byte[] snapshot = new byte[healthOffset + NetHealthSync.HeaderSize]; snapshot[0] = (byte)PacketType.Snapshot;
            new SnapshotHeader { MatchId = 7, AuthorityEpoch = 9, Frame = 1, PlayerCount = 1 }.Write(snapshot.AsSpan(1));
            new PlayerState { SlotIndex = 0, SlotGeneration = 2, LifeId = 1, Health = 99,
                Flags = PlayerState.FlagActive | PlayerState.FlagSpawned }.Write(snapshot.AsSpan(1 + SnapshotHeader.Size));
            BinaryPrimitives.WriteUInt16LittleEndian(snapshot.AsSpan(healthOffset), 7);
            replicaAccept(snapshot, 1);
            byte[] carrierBytes = new byte[2 + IntentPacket.FullSize]; carrierBytes[0] = (byte)PacketType.SlotIntent;
            newer.Write(carrierBytes.AsSpan(2)); replicaAccept(carrierBytes, 102);
            long acceptedBefore = (long)replicaType.GetProperty("AcceptedPackets", All)!.GetValue(replica)!;
            older.Write(carrierBytes.AsSpan(2)); replicaAccept(carrierBytes, 104);
            object[] currentArgs = { 0, default(IntentPacket) };
            bool hasCurrent = (bool)replicaType.GetMethod("TryGetIntent", All)!.Invoke(replica, currentArgs)!;
            Check(hasCurrent && ((IntentPacket)currentArgs[1]).Frame == 102
                && ((IntentPacket)currentArgs[1]).Position.X == 10
                && (long)replicaType.GetProperty("AcceptedPackets", All)!.GetValue(replica)! == acceptedBefore + 1,
                "replica admits older canonical event carrier while current movement stays newest");

            Type streamType = assembly.GetType("MphRead.Mods.Network.ReplayPoseStream", true)!;
            ConstructorInfo constructor = Array.Find(streamType.GetConstructors(All), c => c.GetParameters().Length == 1)!;
            object stream = constructor.Invoke(new object?[] { null });
            streamType.GetField("_supportsFireEvents", All)!.SetValue(stream, true);
            var index = streamType.GetMethod("IndexFireEvents", All)!;
            index.Invoke(stream, new object[] { 102u, 0, newer });
            index.Invoke(stream, new object[] { 104u, 0, older });
            index.Invoke(stream, new object[] { 105u, 0, older });
            var fires = (IDictionary)streamType.GetField("_fires", All)!.GetValue(stream)!;
            var events = new Dictionary<uint, FireEvent>();
            foreach (DictionaryEntry entry in fires)
            {
                foreach (object scheduled in (IList)entry.Value!)
                {
                    var fire = (FireEvent)scheduled.GetType().GetProperty("Event", All)!.GetValue(scheduled)!;
                    Check(events.TryAdd(fire.ShotId, fire), "repeated carriers never index duplicate replay shot");
                }
            }
            Check(events.Count == 3 && events.ContainsKey(41) && events.ContainsKey(42) && events.ContainsKey(43),
                "late lower shot IDs and two authored same-frame shots all reach pose stream");
            Check(events[41].SourceFrame == 100 && events[42].SourceFrame == 100
                && events[41].AckFrame == 79 && events[41].AckSubFrame == 128
                && events[41].Origin == new Vector3(3, 4, 5) && events[41].Charge == 7,
                "replay event indexing preserves original timing, pose, and charge metadata");
            var respawnCarrier = older; respawnCarrier.Frame = 103; respawnCarrier.LifeId = 2;
            respawnCarrier.FireEventCount = 2;
            respawnCarrier.FireEvents[0] = older.FireEvents[0] with { SourceLife = 1 };
            respawnCarrier.FireEvents[1] = older.FireEvents[1] with { ShotId = 44, SourceLife = 1 };
            index.Invoke(stream, new object[] { 107u, 0, respawnCarrier });
            // A separate life may reuse an ID without colliding with its predecessor.
            var newLife = respawnCarrier; newLife.FireEventCount = 1;
            newLife.FireEvents[0] = older.FireEvents[0] with { SourceLife = 2, SourceFrame = 103 };
            index.Invoke(stream, new object[] { 107u, 0, newLife });
            index.Invoke(stream, new object[] { 108u, 0, respawnCarrier });
            int life1Id41 = 0, life2Id41 = 0, oldLifeNewEvent = 0;
            foreach (DictionaryEntry entry in fires)
                foreach (object scheduled in (IList)entry.Value!)
                {
                    var fire = (FireEvent)scheduled.GetType().GetProperty("Event", All)!.GetValue(scheduled)!;
                    ushort life = (ushort)scheduled.GetType().GetProperty("Life", All)!.GetValue(scheduled)!;
                    if (fire.ShotId == 41 && life == 1) life1Id41++;
                    if (fire.ShotId == 41 && life == 2) life2Id41++;
                    if (fire.ShotId == 44 && life == 1) oldLifeNewEvent++;
                }
            Check(life1Id41 == 1 && life2Id41 == 1 && oldLifeNewEvent == 1,
                "replay indexes full life identity and old-life shots carried after respawn exactly once");
            Check(((IList)fires[104u]!).Count == 3,
                "same-frame replay scheduling retains every independent old-life event");
            recorderType.GetMethod("Reset", All)!.Invoke(recorder, null);
            Console.WriteLine("compact intent replay tests passed");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
