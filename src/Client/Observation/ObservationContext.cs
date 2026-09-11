using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using MphRead.Combat;
using MphRead.Entities;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead;

public enum ObservationSourceKind { LiveSpectator, Replay, Killcam }
public enum ObservationObjectiveKind { Flag, Node }
public enum BroadcastFocusKind { None, Player, Objective }

public readonly record struct BroadcastFocus(BroadcastFocusKind Kind, int Id)
{
    public static BroadcastFocus None => default;
    public static BroadcastFocus Player(int slot) => new(BroadcastFocusKind.Player, slot);
    public static BroadcastFocus Objective(int entityId) => new(BroadcastFocusKind.Objective, entityId);
}

public readonly record struct ObservationPlayer(
    int Slot, string Name, Hunter Hunter, int Team, bool Connected, bool Active,
    bool Alive, bool CarriesObjective, bool IsPrime, int Health, BeamType Weapon,
    int AmmoUa, int AmmoMissiles, int Points, int Kills, int Deaths, int Assists,
    Vector3 Position, Vector3 Facing)
{
    /// <summary>
    /// Identity delivered with this scene's snapshot. It is deliberately not
    /// inferred from Slot: slots are reusable across reconnects and lives.
    /// Synthetic contexts may leave this as <see cref="CombatActor.None"/>;
    /// those contexts simply cannot attribute actor-scoped facts.
    /// </summary>
    public CombatActor Identity { get; init; } = CombatActor.None;

    public bool HasIdentity => Identity.IsValid;

    public bool Selectable => Slot is >= 0 and < PlayerEntity.SlotCapacity
        && Connected && Active && Alive;
}

public readonly record struct ObservationObjective(
    int EntityId, ObservationObjectiveKind Kind, Vector3 Position, int Team,
    bool Contested, int CarrierSlot, bool AtBase)
{
    public bool HasCarrier => CarrierSlot is >= 0 and < PlayerEntity.SlotCapacity;
}

/// <summary>
/// Immutable facts belonging to exactly one delivered scene position. Capture
/// never falls back to a live client or another scene, which makes the same
/// type safe for live observation, replay, and killcam presentation.
/// </summary>
public sealed class ObservationContext
{
    public ObservationSourceKind Source { get; }
    public uint DeliveredTick { get; }
    public uint MatchId { get; }
    public uint PhaseRevision { get; }
    public MatchMode Mode { get; }
    public MatchPhase Phase { get; }
    public MatchPeriod Period { get; }
    public float MatchTimeSeconds { get; }
    public ImmutableArray<ObservationPlayer> Players { get; }
    public ImmutableArray<ObservationObjective> Objectives { get; }
    public ImmutableArray<KillFeedEntry> CombatFeedback { get; }
    public ImmutableArray<CombatEvent> CombatEvents { get; }
    public ImmutableArray<WorldEvent> WorldFeedback { get; }
    public ImmutableArray<MatchAward> Awards { get; }
    public ImmutableArray<MatchEvent> SemanticEvents { get; }
    public bool IsOvertime => Period != MatchPeriod.Regulation
        || ContainsWorld(WorldSignalKind.OvertimeStarted)
        || ContainsSemantic(MatchEventKind.OvertimeStarted);
    public bool IsMatchPoint => ContainsWorld(WorldSignalKind.MatchPoint)
        || ContainsSemantic(MatchEventKind.MatchPointReached);

    private ObservationContext(ObservationSourceKind source, uint deliveredTick,
        uint matchId, uint phaseRevision, MatchMode mode, MatchPhase phase,
        MatchPeriod period, float matchTimeSeconds,
        ImmutableArray<ObservationPlayer> players,
        ImmutableArray<ObservationObjective> objectives,
        ImmutableArray<KillFeedEntry> combatFeedback,
        ImmutableArray<CombatEvent> combatEvents,
        ImmutableArray<WorldEvent> worldFeedback,
        ImmutableArray<MatchAward> awards,
        ImmutableArray<MatchEvent> semanticEvents)
    {
        Source = source;
        DeliveredTick = deliveredTick;
        MatchId = matchId;
        PhaseRevision = phaseRevision;
        Mode = mode;
        Phase = phase;
        Period = period;
        MatchTimeSeconds = matchTimeSeconds;
        Players = players;
        Objectives = objectives;
        CombatFeedback = combatFeedback;
        CombatEvents = combatEvents;
        WorldFeedback = worldFeedback;
        Awards = awards;
        SemanticEvents = semanticEvents;
    }

    public static ObservationContext Capture(Scene scene, ScenePresentation presentation,
        ObservationSourceKind source = ObservationSourceKind.LiveSpectator)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(presentation);
        if (!ReferenceEquals(scene, presentation.World))
            throw new ArgumentException("The presentation must belong to the selected scene.", nameof(presentation));

        uint deliveredTick = scene.Services.WorldServerTick;
        if (deliveredTick == 0) deliveredTick = unchecked((uint)scene.FrameCount);
        BroadcastObservationFacts facts = presentation.BroadcastObservations.Snapshot(
            scene.Match.MatchId, scene.Match.PhaseRevision, deliveredTick);

        var players = ImmutableArray.CreateBuilder<ObservationPlayer>(PlayerEntity.SlotCapacity);
        var carrierSlots = new bool[PlayerEntity.SlotCapacity];
        var objectives = ImmutableArray.CreateBuilder<ObservationObjective>();
        foreach (EntityBase entity in scene.Entities)
        {
            if (entity is OctolithFlagEntity flag && entity.Id >= 0)
            {
                int carrier = flag.Carrier?.SlotIndex ?? -1;
                if ((uint)carrier < carrierSlots.Length) carrierSlots[carrier] = true;
                objectives.Add(new(entity.Id, ObservationObjectiveKind.Flag, entity.Position,
                    flag.Data.TeamId, false, carrier, flag.AtBase));
            }
            else if (entity is NodeDefenseEntity node && entity.Id >= 0)
            {
                objectives.Add(new(entity.Id, ObservationObjectiveKind.Node, entity.Position,
                    node.CurrentTeam, node.Contested, -1, false));
            }
        }
        objectives.Sort(static (left, right) => left.EntityId.CompareTo(right.EntityId));

        for (int slot = 0; slot < scene.Players.Count; slot++)
        {
            PlayerEntity player = scene.Players[slot];
            LoadFlags flags = player.LoadFlags;
            (int ua, int missiles) = player.ModAmmo;
            PlayerMatchStats stats = scene.Match.Players[slot];
            ObservationPlayer observed = new ObservationPlayer(slot, scene.Roster.Nicknames[slot] ?? $"Player{slot + 1}",
                player.Hunter, player.TeamIndex,
                flags.TestFlag(LoadFlags.Connected), flags.TestFlag(LoadFlags.Active),
                player.Health > 0 && flags.TestFlag(LoadFlags.Spawned), carrierSlots[slot],
                scene.Match.PrimeHunter == slot, player.Health, player.CurrentWeapon,
                ua, missiles, stats.Points, stats.Kills, stats.Deaths, stats.Assists,
                player.Position, player.FacingVector)
                with { Identity = player.CombatIdentity };
            players.Add(observed);
        }

        var combat = ImmutableArray.CreateBuilder<KillFeedEntry>(presentation.CombatFeedback.FeedCount);
        for (int index = 0; index < presentation.CombatFeedback.FeedCount; index++)
        {
            KillFeedEntry entry = presentation.CombatFeedback.FeedAt(index);
            if (!Sequence32.IsNewer(entry.Tick, deliveredTick)) combat.Add(entry);
        }
        ImmutableArray<WorldEvent> world = IncludeLastDeliveredWorld(
            facts.WorldEvents, presentation.WorldFeedback.LastEvent, scene.Match.MatchId,
            scene.Match.PhaseRevision, deliveredTick);
        return Create(source, deliveredTick, scene.Match.MatchId, scene.Match.PhaseRevision,
            scene.Match.Rules.Mode, scene.Match.Phase, scene.Match.Period, players,
            objectives, combat, world, facts.Awards, facts.SemanticEvents,
            facts.CombatEvents, scene.Match.MatchTime);
    }

    public static ObservationContext Create(ObservationSourceKind source, uint deliveredTick,
        uint matchId, uint phaseRevision, MatchMode mode, MatchPhase phase,
        MatchPeriod period, IEnumerable<ObservationPlayer>? players = null,
        IEnumerable<ObservationObjective>? objectives = null,
        IEnumerable<KillFeedEntry>? combatFeedback = null,
        IEnumerable<WorldEvent>? worldFeedback = null,
        IEnumerable<MatchAward>? awards = null,
        IEnumerable<MatchEvent>? semanticEvents = null,
        IEnumerable<CombatEvent>? combatEvents = null,
        float matchTimeSeconds = -1)
    {
        if (!Enum.IsDefined(source)) throw new ArgumentOutOfRangeException(nameof(source));
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        if (!Enum.IsDefined(phase)) throw new ArgumentOutOfRangeException(nameof(phase));
        if (!Enum.IsDefined(period)) throw new ArgumentOutOfRangeException(nameof(period));
        if (!float.IsFinite(matchTimeSeconds) || matchTimeSeconds < -1)
            throw new ArgumentOutOfRangeException(nameof(matchTimeSeconds));

        ImmutableArray<ObservationPlayer> playerArray = CopyPlayers(players);
        ImmutableArray<ObservationObjective> objectiveArray = CopyObjectives(objectives);
        ImmutableArray<KillFeedEntry> combatArray = CopyDelivered(combatFeedback,
            deliveredTick, static value => value.Tick, static _ => true);
        ImmutableArray<WorldEvent> worldArray = CopyDelivered(worldFeedback,
            deliveredTick, static value => value.Tick,
            value => value.IsValid && value.MatchId == matchId && value.PhaseRevision == phaseRevision);
        ImmutableArray<MatchAward> awardArray = CopyDelivered(awards,
            deliveredTick, static value => value.Tick,
            value => value.IsValid && value.MatchId == matchId && value.PhaseRevision == phaseRevision);
        ImmutableArray<MatchEvent> semanticArray = CopyDelivered(semanticEvents,
            deliveredTick, static value => value.Tick,
            value => value.IsValid && value.MatchId == matchId && value.PhaseRevision == phaseRevision);
        ImmutableArray<CombatEvent> rawCombatArray = CopyDelivered(combatEvents,
            deliveredTick, static value => value.Tick, static value => value.IsValid);
        return new(source, deliveredTick, matchId, phaseRevision, mode, phase, period,
            matchTimeSeconds, playerArray, objectiveArray, combatArray, rawCombatArray,
            worldArray, awardArray, semanticArray);
    }

    public bool TryGetPlayer(int slot, out ObservationPlayer player)
    {
        foreach (ObservationPlayer candidate in Players)
        {
            if (candidate.Slot == slot) { player = candidate; return true; }
        }
        player = default;
        return false;
    }

    /// <summary>
    /// Resolve an actor only against the identity captured in this delivered
    /// context. There is intentionally no slot fallback.
    /// </summary>
    public bool TryGetPlayer(CombatActor actor, out ObservationPlayer player)
    {
        if (!actor.IsValid)
        {
            player = default;
            return false;
        }
        foreach (ObservationPlayer candidate in Players)
        {
            if (candidate.Identity == actor)
            {
                player = candidate;
                return true;
            }
        }
        player = default;
        return false;
    }

    public bool TryGetObjective(int entityId, out ObservationObjective objective)
    {
        foreach (ObservationObjective candidate in Objectives)
        {
            if (candidate.EntityId == entityId) { objective = candidate; return true; }
        }
        objective = default;
        return false;
    }

    public bool IsValidFocus(BroadcastFocus focus) => focus.Kind switch
    {
        BroadcastFocusKind.Player => TryGetPlayer(focus.Id, out ObservationPlayer player) && player.Selectable,
        BroadcastFocusKind.Objective => TryGetObjective(focus.Id, out _),
        _ => false
    };

    private bool ContainsWorld(WorldSignalKind kind)
    {
        foreach (WorldEvent value in WorldFeedback) if (value.Kind == kind) return true;
        return false;
    }

    private bool ContainsSemantic(MatchEventKind kind)
    {
        foreach (MatchEvent value in SemanticEvents) if (value.Kind == kind) return true;
        return false;
    }

    private static ImmutableArray<ObservationPlayer> CopyPlayers(
        IEnumerable<ObservationPlayer>? source)
    {
        var values = source == null ? new List<ObservationPlayer>() : new List<ObservationPlayer>(source);
        if (values.Count > PlayerEntity.SlotCapacity)
            throw new ArgumentException("Observation contexts support at most eight players.", nameof(source));
        values.Sort(static (left, right) => left.Slot.CompareTo(right.Slot));
        int prior = -1;
        foreach (ObservationPlayer value in values)
        {
            if (value.Slot is < 0 or >= PlayerEntity.SlotCapacity || value.Slot == prior
                || value.Name == null || !Finite(value.Position) || !Finite(value.Facing)
                || value.Health < 0 || value.AmmoUa < 0 || value.AmmoMissiles < 0
                || (!value.Identity.IsValid && !value.Identity.IsNone))
                throw new ArgumentException("Invalid observation player.", nameof(source));
            prior = value.Slot;
        }
        return values.ToImmutableArray();
    }

    private static ImmutableArray<ObservationObjective> CopyObjectives(
        IEnumerable<ObservationObjective>? source)
    {
        var values = source == null ? new List<ObservationObjective>() : new List<ObservationObjective>(source);
        if (values.Count > BroadcastObservationJournal.Capacity)
            throw new ArgumentException("Too many observation objectives.", nameof(source));
        values.Sort(static (left, right) => left.EntityId.CompareTo(right.EntityId));
        int? prior = null;
        foreach (ObservationObjective value in values)
        {
            if (value.EntityId < 0 || prior == value.EntityId || !Enum.IsDefined(value.Kind)
                || !Finite(value.Position) || value.CarrierSlot is < -1 or >= PlayerEntity.SlotCapacity)
                throw new ArgumentException("Invalid observation objective.", nameof(source));
            prior = value.EntityId;
        }
        return values.ToImmutableArray();
    }

    private static ImmutableArray<T> CopyDelivered<T>(IEnumerable<T>? source,
        uint deliveredTick, Func<T, uint> tick, Func<T, bool> valid)
    {
        if (source == null) return [];
        var values = new List<T>();
        foreach (T value in source)
        {
            if (valid(value) && !Sequence32.IsNewer(tick(value), deliveredTick)) values.Add(value);
            if (values.Count > BroadcastObservationJournal.Capacity)
                values.RemoveAt(0);
        }
        values.Sort((left, right) => tick(left).CompareTo(tick(right)));
        return values.ToImmutableArray();
    }

    private static ImmutableArray<WorldEvent> IncludeLastDeliveredWorld(
        ImmutableArray<WorldEvent> values, WorldEvent last, uint matchId,
        uint phaseRevision, uint deliveredTick)
    {
        if (!last.IsValid || last.MatchId != matchId || last.PhaseRevision != phaseRevision
            || Sequence32.IsNewer(last.Tick, deliveredTick)) return values;
        foreach (WorldEvent value in values) if (value.Id == last.Id) return values;
        return values.Add(last).Sort(static (left, right) => left.Tick.CompareTo(right.Tick));
    }

    private static bool Finite(Vector3 value)
        => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
