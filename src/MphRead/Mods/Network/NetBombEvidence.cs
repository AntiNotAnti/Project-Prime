using System;
using System.Collections.Generic;
using MphRead.Entities;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network;

/// <summary>Opt-in, process-lifetime observations; never participates in bomb admission.</summary>
internal static class NetBombEvidence
{
    private const int Capacity = 65536;
    private static readonly List<object> Rows = new();
    private static long _overflow;
    internal static long RequestsAuthored { get; private set; }

    private static void Add(object row)
    {
        if (Rows.Count < Capacity) Rows.Add(row);
        else _overflow++;
    }

    private static object Vector(Vector3 value) => new { X = value.X, Y = value.Y, Z = value.Z };
    private static object State(BombState state) => new
    {
        state.Key, Kind = state.Kind.ToString(), state.PlacedFrame,
        Position = Vector(state.Position), Facing = Vector(state.Facing), Up = Vector(state.Up),
        Speed = Vector(state.Speed), state.Countdown, state.Index, state.Link0, state.Link1, state.Recolor
    };

    internal static void Request(BombIntent request, string stage)
    {
        if (!NetCampaignEvidence.Enabled) return;
        if (stage == "requested") RequestsAuthored++;
        Add(new { Stage = stage, Type = "Request", request.Key, Frame = NetSession.NetFrame,
            RequestFrame = request.Frame, Action = request.Action.ToString() });
    }

    internal static void Event(BombEvent fact, string stage)
    {
        if (!NetCampaignEvidence.Enabled) return;
        Add(new { Stage = stage, Type = "Event", fact.State.Key, fact.Sequence, fact.Frame,
            Kind = fact.Kind.ToString(), fact.Reason, State = State(fact.State) });
    }

    internal static void Snapshot(uint frame, ulong frontier, IEnumerable<BombState> rows, string stage)
    {
        if (!NetCampaignEvidence.Enabled) return;
        // One row per snapshot preserves empty snapshots and bounds nested payloads too.
        var states = new List<object>(24);
        foreach (BombState state in rows)
        {
            if (states.Count == 24) { _overflow++; break; }
            states.Add(State(state));
        }
        Add(new { Stage = stage, Type = "Snapshot", Frame = frame, Frontier = frontier,
            States = states.ToArray() });
    }

    internal static void Damage(BombEntity bomb, PlayerEntity victim, uint damageEvent,
        int actualHealthDamage, int actualTurretDamage)
    {
        if (!NetCampaignEvidence.Enabled || victim.SceneServices.IsReplica
            || !(NetSession.IsAuthority || NetSession.IsHost)
            || actualHealthDamage <= 0 && actualTurretDamage <= 0) return;
        Add(new { Stage = "authority-damage", Type = "Damage", Key = bomb.ModOwnerKey,
            Frame = NetSession.NetFrame, DamageEvent = damageEvent, Kind = bomb.BombType.ToString(),
            VictimSlot = victim.SlotIndex, VictimGeneration = NetPlayerLifecycle.Generation(victim.SlotIndex),
            VictimLife = NetPlayerLifecycle.Get(victim.SlotIndex),
            HealthDamage = Math.Max(0, actualHealthDamage), TurretDamage = Math.Max(0, actualTurretDamage),
            HealthAfter = victim.Health, TurretHealthAfter = victim.Halfturret?.Health ?? 0,
            Position = Vector(bomb.Position) });
    }

    public static object Capture() => new { Rows = Rows.ToArray(), Overflow = _overflow };
}
