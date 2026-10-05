using System;
using System.Linq;
using MphRead.Entities;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.Mods.Replay;

/// <summary>
/// Editor-only state and formatting for the authoritative replay combat microscope.
/// Nothing here participates in simulation, collision, damage, RNG or checkpoints.
/// </summary>
internal static class ReplayCombatDiagnostics
{
    internal readonly record struct Selection(
        ushort MatchId,
        ulong AuthorityEpoch,
        byte ShooterSlot,
        ushort ShooterGeneration,
        ushort ShooterLife,
        byte VictimSlot,
        ushort VictimGeneration,
        ushort VictimLife,
        uint ShotId,
        ushort DamageEventId)
    {
        internal bool Matches(in ReplayShotFact fact)
            => fact.MatchId == MatchId
                && fact.AuthorityEpoch == AuthorityEpoch
                && fact.ShooterSlot == ShooterSlot
                && fact.ShooterGeneration == ShooterGeneration
                && fact.ShooterLifeId == ShooterLife
                && fact.VictimSlot == VictimSlot
                && fact.VictimGeneration == VictimGeneration
                && fact.VictimLifeId == VictimLife
                && fact.ShotId == ShotId
                && fact.DamageEventId == DamageEventId;
    }

    internal static bool ShowHud { get; set; }
    internal static bool ShowRays { get; set; }
    internal static Selection? Selected { get; private set; }

    internal static void Select(in ReplayCombatDiagnostic diagnostic)
    {
        ReplayShotFact fact = diagnostic.Fact;
        Selected = new(fact.MatchId, fact.AuthorityEpoch,
            fact.ShooterSlot, fact.ShooterGeneration, fact.ShooterLifeId,
            fact.VictimSlot, fact.VictimGeneration, fact.VictimLifeId,
            fact.ShotId, fact.DamageEventId);
    }

    internal static void ClearSelection() => Selected = null;

    internal static string Describe(in ReplayCombatDiagnostic diagnostic)
    {
        ReplayShotFact fact = diagnostic.Fact;
        string weapon = ReplayStudio.TryBeamType(fact.Weapon, out BeamType beam)
            ? beam.ToString() : $"Weapon {fact.Weapon}";
        string shooter = Name(fact.ShooterSlot);
        string victim = Name(fact.VictimSlot);
        string fireFrame = diagnostic.HasFire
            ? diagnostic.FireRecordingFrame.ToString() : "unknown";
        string ack = double.IsFinite(diagnostic.AckServerFrame)
            ? diagnostic.AckServerFrame.ToString("0.000") : "unavailable";
        string ackWorld = diagnostic.HasAckTarget
            ? $"{Vec(diagnostic.AckTargetPosition)}  impact Δ {diagnostic.AckImpactDistance:0.000}u"
            : "unavailable";

        string pose = diagnostic.HasPose
            ? $"Muzzle {Vec(diagnostic.Fire.Origin)}\n"
                + $"Aim {Vec(diagnostic.Fire.Aim)}  projectile {Vec(diagnostic.Fire.Direction)}\n"
            : "Shot pose unavailable (legacy/recovered evidence).\n";

        string damage = (fact.Flags & ReplayShotFactFlags.HalfturretTarget) != 0
            ? $"{fact.HalfturretDamage} turret damage → {fact.HalfturretHealthAfter} turret HP"
            : $"{fact.Damage} damage → {fact.HealthAfter} HP";

        return $"SHOT #{fact.ShotId}  DAMAGE EVENT #{fact.DamageEventId}  {weapon}\n"
            + $"{shooter} → {victim}  fire frame {fireFrame}  resolve frame {diagnostic.RecordingFrame}\n"
            + $"source frame {fact.LaunchFrame}  resolve tick {fact.ResolveTick}  ACK server {ack}\n"
            + pose
            + $"ACK target {ackWorld}\n"
            + $"Impact {Vec(fact.ImpactPoint)}  {damage}\n"
            + $"WHY HIT: {Explain(fact)}";
    }

    internal static string[] HudLines(in ReplayCombatDiagnostic diagnostic)
    {
        ReplayShotFact fact = diagnostic.Fact;
        string weapon = ReplayStudio.TryBeamType(fact.Weapon, out BeamType beam)
            ? beam.ToString().ToUpperInvariant() : $"W{fact.Weapon}";
        string result = (fact.Flags & ReplayShotFactFlags.HalfturretTarget) != 0
            ? $"{fact.HalfturretDamage} turret dmg"
            : $"{fact.Damage} dmg → {fact.HealthAfter} HP";
        string ack = diagnostic.HasAckTarget
            ? $"ACK Δ {diagnostic.AckImpactDistance:0.00}u"
            : "ACK target unavailable";
        return new[]
        {
            $"COMBAT  SHOT #{fact.ShotId}  EVENT #{fact.DamageEventId}  {weapon}",
            $"{Name(fact.ShooterSlot)} → {Name(fact.VictimSlot)}  {result}",
            $"fire {(diagnostic.HasFire ? diagnostic.FireRecordingFrame.ToString() : "?")}  resolve {diagnostic.RecordingFrame}  {ack}",
            $"WHY: {Explain(fact)}"
        };
    }

    internal static string Explain(in ReplayShotFact fact)
    {
        string basis = (fact.Flags & ReplayShotFactFlags.Claimed) != 0
            ? "validated authority hit-claim rescue"
            : (fact.Flags & ReplayShotFactFlags.Continuous) != 0
                ? "authoritative continuous-target contact"
                : fact.Direct
                    ? "authoritative direct projectile collision"
                    : "authoritative splash / area damage";

        if ((fact.Flags & ReplayShotFactFlags.EnhancedChild) != 0)
            basis += "; enhanced child projectile";
        if ((fact.Flags & ReplayShotFactFlags.HalfturretSource) != 0)
            basis += "; halfturret source";
        if ((fact.Flags & ReplayShotFactFlags.HalfturretTarget) != 0)
            basis += "; halfturret target";
        if (fact.Headshot) basis += "; headshot";
        if (fact.Lethal) basis += "; lethal";
        return basis;
    }

    private static string Name(byte slot)
    {
        if (slot >= PlayerEntity.SlotCapacity) return "world";
        ReplayPlayerInfo? player = DemoPlayback.Metadata?.Players
            .FirstOrDefault(item => item.Slot == slot);
        return player?.Name ?? $"P{slot + 1}";
    }

    internal static string Vec(Vector3 value)
        => $"({value.X:0.00}, {value.Y:0.00}, {value.Z:0.00})";
}
