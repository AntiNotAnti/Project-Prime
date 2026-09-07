using System;
using System.Buffers.Binary;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network
{
    public readonly record struct WorldEvent(uint Id, uint Tick, uint MatchId, uint PhaseRevision,
        WorldSubjectKind Subject, WorldSignalKind Kind, byte Team, uint EntityId,
        CombatActor Actor, Vector3 Position, uint A = 0, uint B = 0, uint C = 0)
    {
        public const int Size = 64;
        public bool IsValid => MatchId != 0 && PhaseRevision != 0 && (Team < 8 || Team == 255)
            && (Actor.IsValid || Actor.IsNone) && float.IsFinite(Position.X) && float.IsFinite(Position.Y) && float.IsFinite(Position.Z)
            && B == 0 && C == 0 && (Kind switch
            {
                WorldSignalKind.PickupConsumed => Subject == WorldSubjectKind.Item && EntityId != 0 && Actor.IsValid && A <= 21,
                WorldSignalKind.PickupRespawned => Subject == WorldSubjectKind.Spawner && Actor.IsNone && Team == 255 && A <= 21,
                WorldSignalKind.FlagPickedUp or WorldSignalKind.FlagDropped or WorldSignalKind.FlagCaptured
                    => Subject == WorldSubjectKind.Flag && Actor.IsValid && A == 0,
                WorldSignalKind.FlagReset => Subject == WorldSubjectKind.Flag && A == 0,
                WorldSignalKind.NodeCaptured => Subject == WorldSubjectKind.Node && Actor.IsValid && Team < 8 && A == 0,
                WorldSignalKind.NodeContested => Subject == WorldSubjectKind.Node && Actor.IsNone && A <= 1,
                WorldSignalKind.PrimeChanged => Subject == WorldSubjectKind.Match && EntityId == 0 && A == 0,
                WorldSignalKind.DefenderStateChanged => Subject == WorldSubjectKind.Node && Actor.IsNone && A <= 1,
                WorldSignalKind.OvertimeStarted => Subject == WorldSubjectKind.Match && EntityId == 0 && Actor.IsNone && Team == 255 && A is 1 or 2,
                WorldSignalKind.MatchPoint => Subject == WorldSubjectKind.Match && EntityId == 0 && Actor.IsNone && Team < 8 && A == 0,
                _ => false
            });
        public void Write(Span<byte> bytes)
        {
            if (bytes.Length != Size || !IsValid) throw new ArgumentException("Invalid world event.");
            bytes.Clear();
            Put(bytes, 0, Id); Put(bytes, 4, Tick); Put(bytes, 8, MatchId); Put(bytes, 12, PhaseRevision);
            bytes[16] = (byte)Subject; bytes[17] = (byte)Kind; bytes[18] = Team;
            Put(bytes, 20, EntityId); bytes[24] = Actor.Slot;
            BinaryPrimitives.WriteUInt64LittleEndian(bytes[25..], Actor.ConnectionId); Put(bytes, 33, Actor.Life);
            Put(bytes, 40, WorldRecord.Bits(Position.X)); Put(bytes, 44, WorldRecord.Bits(Position.Y)); Put(bytes, 48, WorldRecord.Bits(Position.Z));
            Put(bytes, 52, A); Put(bytes, 56, B); Put(bytes, 60, C);
        }
        public static bool TryRead(ReadOnlySpan<byte> bytes, out WorldEvent value)
        {
            value = default;
            if (bytes.Length != Size || (bytes[19] | bytes[37] | bytes[38] | bytes[39]) != 0) return false;
            var parsed = new WorldEvent(Get(bytes, 0), Get(bytes, 4), Get(bytes, 8), Get(bytes, 12),
                (WorldSubjectKind)bytes[16], (WorldSignalKind)bytes[17], bytes[18], Get(bytes, 20),
                new(bytes[24], BinaryPrimitives.ReadUInt64LittleEndian(bytes[25..]), Get(bytes, 33)),
                new(WorldRecord.Float(Get(bytes, 40)), WorldRecord.Float(Get(bytes, 44)), WorldRecord.Float(Get(bytes, 48))),
                Get(bytes, 52), Get(bytes, 56), Get(bytes, 60));
            if (!parsed.IsValid) return false;
            value = parsed; return true;
        }
        private static void Put(Span<byte> bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes[offset..], value);
        private static uint Get(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes[offset..]);
    }
}
