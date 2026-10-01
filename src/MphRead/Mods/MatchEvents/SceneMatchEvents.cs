using MphRead.Entities;
using System;
using MphRead.Mods.Network;
namespace MphRead.Mods.MatchEvents;
/// <summary>Passive Stage A producer. Never touches health, score, wire or player-facing consumers.</summary>
internal sealed class SceneMatchEvents
{
    internal MatchSemanticEventBus Bus { get; } = new();
    private readonly MatchAssistTracker _assists = new();
    private ushort _offlineMatch = 1;
    private bool _started, _countdown, _identityExhausted;
    private readonly PlayerEntity?[] _occupants = new PlayerEntity?[8];
    private readonly ushort[] _generations = new ushort[8];
    private readonly ushort[] _lives = new ushort[8];
    internal long DeathTransitions { get; private set; }
    internal long SemanticDeaths { get; private set; }
    internal long HeadshotTransitions { get; private set; }
    internal long SemanticHeadshots { get; private set; }
    internal long RejectedEvents { get; private set; }
    internal MatchSemanticParity ParityDiagnostics { get; } = new();
    internal bool GenerationHealthy => DeathTransitions == SemanticDeaths && HeadshotTransitions == SemanticHeadshots;
    private bool Prepare(Scene scene)
    {
        // Check replica first: replica scenes must not consult live session identity.
        if (scene.Services.IsReplica || !scene.GameState.Multiplayer
            || (NetSession.Active && !NetSession.IsAuthority)) return false;
        if (_identityExhausted || (!NetSession.Active && scene.FrameCount > uint.MaxValue)) return false;
        if (NetSession.Active && (NetSession.CurrentMatchId == 0 || NetSession.AuthorityEpoch == 0)) return false;
        ushort match = NetSession.Active ? NetSession.CurrentMatchId : _offlineMatch;
        ulong epoch = NetSession.Active ? NetSession.AuthorityEpoch : 1UL;
        if (match != Bus.MatchId || epoch != Bus.AuthorityEpoch)
        {
            _started = _countdown = false;
            ParityDiagnostics.Reset();
            _assists.Reset();
        }
        Bus.Begin(NetSession.Active ? NetSession.CurrentMatchId : _offlineMatch,
            NetSession.Active ? NetSession.AuthorityEpoch : 1UL);
        return true;
    }
    private MatchSemanticActor Actor(PlayerEntity? player)
    {
        if (player == null || (uint)player.SlotIndex >= 8) return MatchSemanticActor.None;
        int slot = player.SlotIndex;
        if (NetSession.Active) return new((byte)slot, NetPlayerLifecycle.Generation(slot), NetPlayerLifecycle.Get(slot));
        if (_occupants[slot] != player)
        {
            if (_generations[slot] == ushort.MaxValue) { _identityExhausted = true; return default; }
            _occupants[slot] = player;
            _generations[slot]++; _lives[slot] = 1;
        }
        return new((byte)slot, _generations[slot], _lives[slot]);
    }
    private MatchSemanticActor SourceActor(PlayerEntity? player, ShotKey? launch)
        => NetSession.Active && launch is { Generation: > 0, LifeId: > 0 } key
            && key.MatchId == Bus.MatchId && key.AuthorityEpoch == Bus.AuthorityEpoch
            ? new((byte)key.ShooterSlot, key.Generation, key.LifeId) : Actor(player);
    internal void ObservePhase(Scene scene)
    {
        if (!Prepare(scene)) return;
        if (NetSession.Active && NetSession.IsStarting && !_countdown)
        {
            _countdown = true;
            Transition(scene, MatchSemanticEventType.CountdownStarted);
        }
        if (!_started && (!NetSession.Active || !NetSession.FreezeGameplay))
        {
            _started = true;
            Transition(scene, MatchSemanticEventType.MatchStarted);
        }
        if (NetSession.Active) ParityDiagnostics.Advance(Tick(scene));
    }
    internal void ResetMatch(Scene scene)
    {
        if (scene.Services.IsReplica || NetSession.Active) return;
        if (_offlineMatch == ushort.MaxValue) { _identityExhausted = true; RejectedEvents++; return; }
        _offlineMatch++;
    }
    internal void Transition(Scene scene, MatchSemanticEventType type, PlayerEntity? actor = null,
        PlayerEntity? target = null, int entity = -1, int value = 0, int weapon = -1, MatchSemanticEventFlags flags = 0)
    {
        if (!Prepare(scene)) return;
        if (!Bus.TryEmit(Tick(scene), type, Actor(actor), Actor(target), weapon: weapon, flags: flags, entity: entity, value: value)) RejectedEvents++;
    }
    private static uint Tick(Scene scene) => NetSession.Active ? NetSession.NetFrame : checked((uint)scene.FrameCount);
    internal void Spawn(Scene scene, PlayerEntity player)
    {
        if (!Prepare(scene)) return;
        bool newOccupant = _occupants[player.SlotIndex] != player;
        var actor = Actor(player);
        if (!NetSession.Active && !newOccupant)
        {
            if (_lives[player.SlotIndex] == ushort.MaxValue) { _identityExhausted = true; RejectedEvents++; return; }
            actor = actor with { Life = ++_lives[player.SlotIndex] };
        }
        if (!Bus.TryEmit(Tick(scene), MatchSemanticEventType.PlayerSpawned, actor, MatchSemanticActor.None)) RejectedEvents++;
        else _assists.Spawn(actor);
    }
    internal void Damage(Scene scene, PlayerEntity? attacker, PlayerEntity victim, uint damage, ShotKey? launch = null)
    {
        if (!Prepare(scene) || attacker == null || attacker == victim || victim.Health <= 0) return;
        bool opposing = !scene.GameState.Teams || !Mods.Multiplayer.TeamRules.AreAllies(attacker.TeamIndex, victim.TeamIndex);
        _assists.Damage(SourceActor(attacker, launch), Actor(victim), Tick(scene), damage, opposing);
    }
    internal void Headshot(Scene scene, PlayerEntity? attacker, PlayerEntity victim, BeamType weapon, ShotKey? launch = null)
    {
        if (!Prepare(scene)) return;
        HeadshotTransitions++;
        if (Bus.TryEmit(Tick(scene), MatchSemanticEventType.Headshot, SourceActor(attacker, launch), Actor(victim), (int)weapon,
            MatchSemanticEventFlags.Headshot))
        {
            SemanticHeadshots++;
            if (NetSession.Active) ParityDiagnostics.Semantic(Bus.LastEvent);
        }
        else RejectedEvents++;
    }
    internal void Death(Scene scene, PlayerEntity? attacker, PlayerEntity victim, BeamType weapon, DamageFlags damage, ShotKey? launch = null)
    {
        if (!Prepare(scene)) return;
        DeathTransitions++;
        var flags = damage.TestFlag(DamageFlags.Headshot) ? MatchSemanticEventFlags.Headshot : MatchSemanticEventFlags.None;
        if (victim.IsPrimeHunter) flags |= MatchSemanticEventFlags.Prime;
        if (victim.OctolithFlag != null) flags |= MatchSemanticEventFlags.Carrier;
        if (attacker == null) flags |= MatchSemanticEventFlags.Environment;
        if (attacker != null && attacker != victim && scene.GameState.Teams
            && Mods.Multiplayer.TeamRules.AreAllies(attacker.TeamIndex, victim.TeamIndex)) flags |= MatchSemanticEventFlags.FriendlyFire;
        if (Bus.TryEmit(Tick(scene), attacker == victim ? MatchSemanticEventType.PlayerSuicide : MatchSemanticEventType.PlayerKilled,
            attacker == victim ? Actor(victim) : SourceActor(attacker, launch), Actor(victim), (int)weapon, flags))
        {
            SemanticDeaths++;
            if (NetSession.Active) ParityDiagnostics.Semantic(Bus.LastEvent);
            Span<MatchSemanticActor> contributors = stackalloc MatchSemanticActor[8];
            int count = _assists.Collect(Actor(victim), SourceActor(attacker, launch), Tick(scene), contributors);
            for (int i = 0; i < count; i++)
            {
                var contributor = contributors[i];
                ushort generation = NetSession.Active ? NetPlayerLifecycle.Generation(contributor.Slot) : _generations[contributor.Slot];
                if (generation != contributor.SlotGeneration) continue; // disconnected/reused occupant cannot receive an assist
                if (!Bus.TryEmit(Tick(scene), MatchSemanticEventType.PlayerAssisted, contributor, Actor(victim))) RejectedEvents++;
            }
        }
        else RejectedEvents++;
    }
}
