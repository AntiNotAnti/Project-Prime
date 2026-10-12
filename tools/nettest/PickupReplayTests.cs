using System;
using System.Buffers.Binary;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using MphRead.Entities;
using MphRead.Mods.Replay;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

internal static class PickupReplayTests
{
    private static int _checks;
    private static void Check(bool value, string label)
    { _checks++; if (!value) throw new InvalidOperationException("Pickup replay: " + label); }
    private static void Reject(Action action, string label)
    { bool refused = false; try { action(); } catch (InvalidDataException) { refused = true; } Check(refused, label); }
    private static byte[] Packet(PickupClaim value)
    { var b = new byte[1 + PickupClaim.Size]; b[0] = (byte)PacketType.PickupClaim; value.Write(b.AsSpan(1)); return b; }
    private static byte[] Packet(PickupVerdict value)
    { var b = new byte[1 + PickupVerdict.Size]; b[0] = (byte)PacketType.PickupVerdict; value.Write(b.AsSpan(1)); return b; }
    private static byte[] Packet(PickupItemFact value)
    { var b = new byte[1 + PickupItemFact.Size]; b[0] = (byte)PacketType.PickupItem; value.Write(b.AsSpan(1)); return b; }
    private static ReplayReplicaState State()
    {
        var state = new ReplayReplicaState(); var match = new MatchStatePacket
        { MatchId = 2, AuthorityEpoch = 9, RoomKey = "unit-pickup-replay", Mode = (byte)GameMode.Battle };
        var bytes = new byte[1 + MatchStatePacket.Size]; bytes[0] = (byte)PacketType.MatchState; match.Write(bytes.AsSpan(1));
        state.Accept(bytes, 0); return state;
    }
    private static void LongItemHistory()
    {
        var state=State();
        var live=new PickupItemFact(new(9,2,7,10,1),TimedPowerUp.DoubleDamage,1,true,new Vector3(1,1,0));
        state.Accept(Packet(live),1);
        PickupItemFact pruned=default;
        for(uint generation=2;generation<=3000;generation++)
        {
            var birth=live with { Spawn=live.Spawn with { Generation=generation },AuthorityFrame=generation*2 };
            state.Accept(Packet(birth),generation*2);
            state.Accept(Packet(birth with { Available=false,Position=new Vector3(2,1,0) }),generation*2);
            if(generation==2) pruned=birth;
        }
        Check(state.PickupItems.Count<PickupItemHistory.Capacity && state.PickupItems[live.Spawn]==live,
            "long-room replay bounds recent tombstones while retaining old active item");
        state.Accept(Packet(pruned),6001);
        Check(!state.PickupItems.ContainsKey(pruned.Spawn), "pruned old birth cannot resurrect in replay");
        // Old active entry was inserted first; later dictionary entries carry a
        // much newer watermark. Restore must not depend on serialized order.
        var checkpoint=state.CaptureCheckpoint(); var restored=State(); restored.RestoreCheckpoint(checkpoint);
        Check(restored.PickupItems[live.Spawn]==live && restored.PickupItems.Count==state.PickupItems.Count
            && restored.CaptureCheckpoint().Bytes.SequenceEqual(checkpoint.Bytes),
            "checkpoint restores old active first and newer retained entries with exact roundtrip");
        var refreshed=live with { AuthorityFrame=6002,Position=new Vector3(3,1,0) };
        restored.Accept(Packet(refreshed),6002);
        Check(restored.PickupItems[live.Spawn]==refreshed, "late-join refreshed current old item accepted after restore");
        var taken=refreshed with { Available=false,Position=new Vector3(4,1,0) };
        restored.Accept(Packet(taken),6003);
        Check(restored.PickupItems[live.Spawn]==taken, "same authority frame true-to-false retains moved final position");
        Reject(()=>restored.Accept(Packet(refreshed with { AuthorityFrame=6003 }),6004),
            "newer false-to-true contradicts authoritative availability explicitly");
    }

    private static void ItemAppendix()
    {
        var state = State();
        var item = (ItemInstanceEntity)RuntimeHelpers.GetUninitializedObject(typeof(ItemInstanceEntity));
        typeof(ItemInstanceEntity).GetField("<ItemType>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(item, ItemType.DoubleDamage);
        item.Position = new Vector3(1, 2, 3); item.ModPickupSpawn = new(9, 2, 7, -1, 8); item.ModPickupHidden = true;
        var graphType = typeof(ReplayWorldCheckpoint).GetNestedType("CaptureGraph", BindingFlags.NonPublic)!;
        object graph = Activator.CreateInstance(graphType, nonPublic: true)!;
        graphType.GetMethod("Id", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(graph, new object[] { item });
        var write = typeof(ReplayWorldCheckpoint).GetMethod("WritePickupItems", BindingFlags.Static | BindingFlags.NonPublic)!;
        var read = typeof(ReplayWorldCheckpoint).GetMethod("ReadPickupItems", BindingFlags.Static | BindingFlags.NonPublic)!;
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        write.Invoke(null, new object[] { writer, graph, state }); writer.Flush(); byte[] bytes = stream.ToArray();
        var key = item.ModPickupSpawn; item.ModPickupSpawn = null; item.ModPickupHidden = false;
        void Load(byte[] value, object[] objects)
        {
            try { read.Invoke(null, new object[] { value, objects, state }); }
            catch (TargetInvocationException ex) when (ex.InnerException is InvalidDataException)
            { throw (InvalidDataException)ex.InnerException; }
        }
        Load(bytes, new object[] { item });
        Check(item.ModPickupSpawn == key && item.ModPickupHidden,
            "actual outer item appendix restores private identity and hidden presentation without base schema changes");
        byte[] invalid = (byte[])bytes.Clone(); invalid[^1] = 2;
        Reject(() => Load(invalid, new object[] { item }), "outer item appendix rejects noncanonical hidden boolean");
        Check(item.ModPickupSpawn == key && item.ModPickupHidden, "invalid item appendix does not partially mutate existing presentation");
        Reject(() => Load(bytes, new object[] { new object() }), "outer item graph id requires actual item type");
        byte[] duplicates = new byte[bytes.Length + bytes.Length - 4];
        bytes.CopyTo(duplicates, 0); bytes.AsSpan(4).CopyTo(duplicates.AsSpan(bytes.Length));
        BinaryPrimitives.WriteInt32LittleEndian(duplicates, 2);
        Reject(() => Load(duplicates, new object[] { item }), "outer item appendix rejects duplicate graph identity");
        Load(new byte[4], new object[] { item });
        Check(item.ModPickupSpawn == null && !item.ModPickupHidden,
            "empty item appendix clears unlisted optional fields without resurrecting visual state");
    }
    internal static int Run()
    {
        _checks = 0;
        try
        {
            var state = State(); var other = State();
            var claim = new PickupClaim(new(9, 2, 3, 4, 5, 6), new(9, 2, 7, -1, 8),
                TimedPowerUp.DoubleDamage, 91, new Vector3(1, 2, 3));
            var verdict = new PickupVerdict(claim, PickupResult.Taken, 105, 101, 0, 0, 0);
            var item = new PickupItemFact(claim.Spawn, claim.Type, 100, true, claim.Position);
            state.Accept(Packet(claim), 110); state.Accept(Packet(verdict), 111); state.Accept(Packet(item), 112);
            Check(state.PickupClaims[claim.Key] == claim && state.PickupVerdicts[claim.Key] == verdict
                && state.PickupItems[item.Spawn] == item, "actual replica intake preserves identity and refused terminal without grants");
            Check(other.PickupClaims.Count == 0 && other.PickupVerdicts.Count == 0 && other.PickupItems.Count == 0,
                "independent replica has no shared pickup ledger");
            state.Accept(Packet(claim), 113); state.Accept(Packet(verdict), 113);
            Check(state.PickupClaims.Count == 1 && state.PickupVerdicts.Count == 1, "identical retries do not duplicate terminals");
            Reject(() => state.Accept(Packet(claim with { Position = Vector3.Zero }), 114), "conflicting claim rejected");
            Reject(() => state.Accept(Packet(verdict with { Result = PickupResult.Gone }), 114), "conflicting terminal rejected");
            var moved = item with { AuthorityFrame = 101, Position = Vector3.Zero };
            state.Accept(Packet(moved), 115); state.Accept(Packet(item), 116);
            Check(state.PickupItems[item.Spawn] == moved, "newer position update wins and stale item does not rewind");
            var taken = moved with { Available = false, Position = new Vector3(4, 5, 6) }; state.Accept(Packet(taken), 117);
            Check(state.PickupItems[item.Spawn] == taken, "same-frame moving contact may terminate availability at its new position");
            state.Accept(Packet(taken with { Position = Vector3.One }), 118);
            Check(state.PickupItems[item.Spawn] == taken, "same-frame deferred removal after parent movement keeps consumed identity");
            Reject(() => state.Accept(Packet(taken with { AuthorityFrame = 103, Available = true }), 118), "taken identity cannot resurrect");
            var checkpoint = state.CaptureCheckpoint(); other.RestoreCheckpoint(checkpoint);
            Check(other.PickupClaims[claim.Key] == claim && other.PickupVerdicts[claim.Key] == verdict
                && other.PickupItems[item.Spawn] == taken, "v10 checkpoint roundtrips all three private data types");
            byte[] before = other.CaptureCheckpoint().Bytes.ToArray();
            byte[] truncated = checkpoint.Bytes[..^1].ToArray();
            Reject(() => other.RestoreCheckpoint(new ReplayReplicaCheckpoint(truncated)), "truncated appendix refused");
            Check(other.CaptureCheckpoint().Bytes.SequenceEqual(before), "failed restore leaves complete replica unchanged");
            // A v9 layout is the unchanged prefix of an empty v10 appendix.
            byte[] empty = State().CaptureCheckpoint().Bytes.ToArray();
            byte[] schemaTen = empty.AsSpan(0, empty.Length - 87).ToArray();
            BinaryPrimitives.WriteUInt16LittleEndian(schemaTen.AsSpan(4), 10); schemaTen[6] = 53;
            var oldTen = State(); oldTen.RestoreCheckpoint(new ReplayReplicaCheckpoint(schemaTen));
            Check(!oldTen.TimedGrants.HasCoverage, "v10 checkpoint has unknown grant presentation coverage");
            byte[] old = empty.AsSpan(0, empty.Length - 16 - 87).ToArray();
            BinaryPrimitives.WriteUInt16LittleEndian(old.AsSpan(4), 9);
            other.RestoreCheckpoint(new ReplayReplicaCheckpoint(old));
            Check(other.PickupClaims.Count == 0 && other.PickupVerdicts.Count == 0 && other.PickupItems.Count == 0,
                "historical v9 prefix restores empty A2 state and clears existing facts");
            Check(ReplayIdentityCompatibility.Convert(Packet(claim), 52).IsEmpty
                && ReplayIdentityCompatibility.Convert(Packet(verdict), 52).IsEmpty
                && ReplayIdentityCompatibility.Convert(Packet(item), 52).IsEmpty, "pre53 protocol excludes introduced pickup packets");
            state.Accept(Packet(claim with { Spawn = claim.Spawn with { Room = 8 } }), 120);
            Check(state.PickupClaims.Count == 1 && state.PickupItems.Count == 0 && state.PickupVerdicts.Count == 0,
                "new room clears older private room identities");
            state.Accept(Packet(verdict), 121);
            Check(state.PickupVerdicts.Count == 0, "delayed old room cannot restore retired terminal");
            var bounded = State();
            for (uint i = 1; i <= ReplayReplicaState.PickupReplayCapacity; i++)
                bounded.Accept(Packet(claim with { Key = claim.Key with { Sequence = i } }), i);
            Reject(() => bounded.Accept(Packet(claim with { Key = claim.Key with { Sequence = (uint)ReplayReplicaState.PickupReplayCapacity + 1 } }), 2050), "claim budget rejects without eviction");
            Check(bounded.PickupClaims.Count == ReplayReplicaState.PickupReplayCapacity && bounded.PickupClaims.ContainsKey(claim.Key with { Sequence = 1 }),
                "bounded identity store retains earliest key at capacity");
            LongItemHistory();
            ItemAppendix();
            Console.WriteLine($"PICKUP REPLAY PASS: {_checks} checks"); return 0;
        }
        catch (Exception ex) { Console.WriteLine("PICKUP REPLAY FAIL: " + ex); return 1; }
    }
}
