using System;
using System.Buffers.Binary;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network;

internal enum CombatImpactKind : byte { Unknown, Direct, Splash, Continuous, Child, Turret }
internal readonly record struct ImpactPresentationData(CombatImpactKind Kind, uint Component,
    bool HasBodyOffset, Vector3 BodyOffset)
{
    internal static ImpactPresentationData Unknown => default;
}
internal readonly record struct LiveCombatImpact(ReplayShotFact Fact, ImpactPresentationData Presentation)
{
    internal CombatImpactIdentity Identity => CombatImpactIdentity.From(Fact, Presentation.Component);
    internal bool Matches(ushort match, ulong epoch, ushort shooterGeneration, ushort shooterLife,
        ushort victimGeneration, ushort victimLife) => Fact.MatchId == match && Fact.AuthorityEpoch == epoch
        && Fact.ShooterGeneration == shooterGeneration && Fact.ShooterLifeId == shooterLife
        && Fact.VictimGeneration == victimGeneration && Fact.VictimLifeId == victimLife;
}

internal static class LiveCombatImpactPacket
{
    internal const byte Version = 1;
    internal const int Size = 74;
    internal const float OffsetScale = 4096f; // <= 1/8192 world units of rounding per axis.
    internal static bool TryQuantize(Vector3 offset, out short x, out short y, out short z)
    {
        x = y = z = 0;
        if (!float.IsFinite(offset.X) || !float.IsFinite(offset.Y) || !float.IsFinite(offset.Z)
            || MathF.Abs(offset.X) > 7.999f || MathF.Abs(offset.Y) > 7.999f || MathF.Abs(offset.Z) > 7.999f) return false;
        x = (short)MathF.Round(offset.X * OffsetScale); y = (short)MathF.Round(offset.Y * OffsetScale);
        z = (short)MathF.Round(offset.Z * OffsetScale); return true;
    }
    internal static void Write(in LiveCombatImpact impact, Span<byte> dest)
    {
        if (dest.Length < Size) throw new ArgumentException("Impact buffer too short", nameof(dest));
        dest[0] = Version; ReplayShotFactPacket.Write(impact.Fact, dest[1..]);
        var p = impact.Presentation; dest[62] = (byte)p.Kind;
        BinaryPrimitives.WriteUInt32LittleEndian(dest[63..], p.Component);
        short x = 0, y = 0, z = 0;
        bool known = p.HasBodyOffset && TryQuantize(p.BodyOffset, out x, out y, out z);
        dest[67] = known ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteInt16LittleEndian(dest[68..], known ? x : (short)0);
        BinaryPrimitives.WriteInt16LittleEndian(dest[70..], known ? y : (short)0);
        BinaryPrimitives.WriteInt16LittleEndian(dest[72..], known ? z : (short)0);
    }
    internal static bool TryRead(ReadOnlySpan<byte> src, out LiveCombatImpact impact)
    {
        impact = default;
        if (src.Length != Size || src[0] != Version || src[62] > (byte)CombatImpactKind.Turret || src[67] > 1
            || !ReplayShotFactPacket.TryRead(src.Slice(1, ReplayShotFactPacket.Size), out var fact)) return false;
        short x = BinaryPrimitives.ReadInt16LittleEndian(src[68..]), y = BinaryPrimitives.ReadInt16LittleEndian(src[70..]),
            z = BinaryPrimitives.ReadInt16LittleEndian(src[72..]);
        bool known = src[67] == 1;
        if (x == short.MinValue || y == short.MinValue || z == short.MinValue
            || !known && (x != 0 || y != 0 || z != 0)) return false;
        var kind = (CombatImpactKind)src[62];
        // Offset describes a certified body contact, not the explosion's centre.
        if (known && (kind != CombatImpactKind.Direct || !fact.Direct
            || (fact.Flags & (ReplayShotFactFlags.Claimed | ReplayShotFactFlags.HalfturretTarget)) != 0)) return false;
        if (kind == CombatImpactKind.Direct && !fact.Direct
            || kind == CombatImpactKind.Splash && fact.Direct
            || kind == CombatImpactKind.Continuous && (fact.Flags & ReplayShotFactFlags.Continuous) == 0) return false;
        impact = new(fact, new(kind, BinaryPrimitives.ReadUInt32LittleEndian(src[63..]), known,
            new Vector3(x, y, z) / OffsetScale));
        return true;
    }
}

internal static class NetCombatFactPublisher
{
    // Opt-in until rendered and impaired acceptance passes. Independent of recorder admission.
    internal static bool LiveEnabled { get; set; }
    private static readonly ImpactIdentityWindow Published = new();
    internal static void Reset() => Published.Reset();
    internal static void Publish(in ReplayShotFact fact, in ImpactPresentationData presentation)
    {
        if (NetSession.Role != NetRole.Server || !NetSession.IsAuthority || DemoPlayback.IsActive
            || !NetSession.MatchesStream(fact.MatchId, fact.AuthorityEpoch)) return;
        if (!LiveEnabled && !NetImpactDiagnostics.Enabled)
        { ReplayCapture.AcceptedShotFact(fact); return; }
        if (!Published.Add(CombatImpactIdentity.From(fact, presentation.Component))) return;
        NetImpactDiagnostics.Record(fact, ImpactStage.Authority, presentation.Component);
        if (LiveEnabled)
        {
            Span<byte> bytes = stackalloc byte[LiveCombatImpactPacket.Size];
            LiveCombatImpactPacket.Write(new(fact, presentation), bytes);
            if (LiveCombatImpactPacket.TryRead(bytes, out _)) NetSession.LiveImpactSink?.Invoke(bytes);
        }
        ReplayCapture.AcceptedShotFact(fact);
    }
}
