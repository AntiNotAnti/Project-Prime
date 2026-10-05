using System;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods.Multiplayer;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.Mods.EnhancedHunters;

/// <summary>All gameplay entry points gate authority before changing enhanced state.</summary>
internal static class EnhancedHunters
{
    [ThreadStatic] private static int _bonusDepth;
    internal static bool ApplyingBonus => _bonusDepth != 0;
    internal static bool Enabled(PlayerEntity player) => player.Hunter != Hunter.Guardian
        && player.OwningScene.GameState.EnhancedHunters
        && !player.OwningScene.GameState.BalancedMode
        && player.OwningScene.GameState.Multiplayer;
    internal static bool Authority(PlayerEntity player) => Enabled(player)
        && !player.SceneServices.IsReplica
        && (!player.SceneServices.PlayerReplication.Active || player.SceneServices.PlayerReplication.IsAuthority);
    internal static bool UsingAffinity(PlayerEntity player, BeamType beam) => Enabled(player)
        && Weapons.GetAffinityBeam(player.Hunter) == beam;
    internal static bool Alive(PlayerEntity player) => player.Health > 0
        && player.LoadFlags.TestFlag(LoadFlags.Active) && !player.Flags2.TestFlag(PlayerFlags2.Spectating);
    internal static bool Hostile(PlayerEntity owner, PlayerEntity target) => owner != target && Alive(target)
        && (!owner.OwningScene.GameState.Teams || owner.OwningScene.GameState.FriendlyFire
            || !TeamRules.AreAllies(owner.TeamIndex, target.TeamIndex));
    internal static ushort Life(PlayerEntity player) => player.SceneServices.PlayerReplication.Active
        ? player.SceneServices.PlayerReplication.IsReplica
            ? player.SceneServices.PlayerReplication.TryGetState(player.SlotIndex, out var state) ? state.LifeId : (ushort)0
            : NetPlayerLifecycle.Get(player.SlotIndex)
        : player.EnhancedState.LocalLife;
    internal static ushort Generation(PlayerEntity player) => player.SceneServices.PlayerReplication.Active
        ? player.SceneServices.PlayerReplication.IsReplica
            ? player.SceneServices.PlayerReplication.TryGetState(player.SlotIndex, out var state) ? state.SlotGeneration : (ushort)0
            : NetPlayerLifecycle.Generation(player.SlotIndex)
        : (ushort)1;
    internal static void Mark(PlayerEntity owner, PlayerEntity target, int frames)
    {
        var s = owner.EnhancedState;
        s.TargetSlot = (byte)target.SlotIndex; s.TargetLifeId = Life(target);
        s.TargetGeneration = Generation(target); s.TimerA = frames;
    }
    internal static bool HasMark(PlayerEntity owner, PlayerEntity target) => owner.EnhancedState.TargetSlot == target.SlotIndex
        && owner.EnhancedState.TargetLifeId == Life(target) && owner.EnhancedState.TargetGeneration == Generation(target);
    internal static PlayerEntity? Target(PlayerEntity owner)
    {
        var s = owner.EnhancedState;
        if (s.TargetSlot >= owner.OwningScene.Players.Items.Count) return null;
        var target = owner.OwningScene.Players.Items[s.TargetSlot];
        return Hostile(owner, target) && Life(target) == s.TargetLifeId
            && Generation(target) == s.TargetGeneration ? target : null;
    }
    internal static bool Visible(PlayerEntity owner, PlayerEntity target) => Visible(owner.OwningScene,
        owner.Position.AddY(.5f), target.Position.AddY(.5f));
    internal static bool Visible(Scene scene, Vector3 from, Vector3 to)
    {
        CollisionResult result = default;
        var candidates = CollisionDetection.GetCandidatesForLimits(from, to, 0,
            null, Vector3.Zero, includeEntities: true, scene);
        return !CollisionDetection.CheckBetweenPoints(candidates, from, to, TestFlags.Beams, scene, ref result);
    }
    internal static bool InCone(PlayerEntity player, PlayerEntity target, float range, float degrees)
    {
        Vector3 delta = target.Position - player.Position;
        return delta.LengthSquared > .0001f && delta.LengthSquared <= range * range
            && Vector3.Dot(delta.Normalized(), player.FacingVector.Normalized()) >= MathF.Cos(degrees * MathF.PI / 180)
            && Visible(player, target);
    }
    internal static void ResetPlayer(PlayerEntity player)
    {
        player.EnhancedState.Reset();
        player.EnhancedState.Hunter = player.Hunter;
        foreach (var other in player.OwningScene.Players.Items)
        {
            other.EnhancedState.FrostExpiry[player.SlotIndex] = 0;
            other.EnhancedState.CrossfireExpiry[player.SlotIndex] = 0;
            if (other != player && other.EnhancedState.TargetSlot == player.SlotIndex)
                other.EnhancedState.ClearTarget();
        }
    }
    internal static void OnPlayerSpawn(PlayerEntity player)
    {
        ResetPlayer(player);
        player.EnhancedState.LocalLife++;
        if (player.EnhancedState.LocalLife == 0) player.EnhancedState.LocalLife = 1;
    }
    internal static void OnPlayerFrame(PlayerEntity player)
    {
        var s = player.EnhancedState;
        if (!Enabled(player) || !Alive(player) || s.Hunter != player.Hunter)
        { ResetPlayer(player); return; }
        player.OwningScene.EnhancedWorld.Tick(player.OwningScene);
        bool moves = !player.SceneServices.IsReplica && (!player.SceneServices.PlayerReplication.Active
            || player.SceneServices.PlayerReplication.LocalSlot == player.SlotIndex || player.IsBot);
        if (moves)
        {
            if (s.MovementCooldown > 0) s.MovementCooldown--;
            player.OwningScene.EnhancedWorld.Movement(player);
            if (player.Hunter == Hunter.Sylux) SyluxEnhancement.Movement(player);
        }
        if (!Authority(player)) return;
        EnhancedHunterTelemetry.Event(player.Hunter, "enabled-frames");
        if (s.TargetSlot != byte.MaxValue && Target(player) == null) s.ClearTarget();
        if (s.TimerA > 0 && --s.TimerA == 0 && player.Hunter != Hunter.Weavel) s.ClearTarget();
        if (s.TimerB > 0) s.TimerB--;
        if (s.GhostFrames > 0) s.GhostFrames--;
        if (s.CrossfireCooldown > 0) s.CrossfireCooldown--;
        bool attacking = player.Flags2.TestFlag(PlayerFlags2.AltAttack);

        if (attacking && !s.WasAttacking) OnAltAttackStarted(player);
        s.WasAlt = player.IsAltForm; s.WasAttacking = attacking;
        switch (player.Hunter)
        {
            case Hunter.Samus: SamusEnhancement.Frame(player); break;
            case Hunter.Sylux: SyluxEnhancement.Frame(player); break;
            case Hunter.Weavel: WeavelEnhancement.Frame(player); break;
        }
    }
    internal static void OnConfirmedHit(PlayerEntity attacker, PlayerEntity victim, BeamType beam,
        bool direct, bool charged, bool headshot, bool turret = false, bool wasFrozen = false)
    {
        if (ApplyingBonus || !Authority(attacker) || attacker == victim
            || attacker.OwningScene.GameState.Teams && !attacker.OwningScene.GameState.FriendlyFire
                && TeamRules.AreAllies(attacker.TeamIndex, victim.TeamIndex)) return;
        if (turret) { WeavelEnhancement.TurretHit(attacker, victim); return; }
        if (beam == BeamType.None)
        {
            if (direct) OnAltAttackHit(attacker, victim);
            return;
        }
        if (!direct || !UsingAffinity(attacker, beam)) return;
        switch (attacker.Hunter)
        {
            case Hunter.Kanden: KandenEnhancement.Hit(attacker, victim, charged); break;
            case Hunter.Trace: TraceEnhancement.Hit(attacker, victim, headshot); break;
            case Hunter.Sylux: SyluxEnhancement.Hit(attacker, victim); break;
            case Hunter.Noxus: NoxusEnhancement.Hit(attacker, victim, charged, wasFrozen); break;
            case Hunter.Weavel: WeavelEnhancement.Hit(attacker); break;
        }
    }
    internal static void OnEnterAlt(PlayerEntity player)
    {
        if (player.Hunter == Hunter.Weavel) WeavelEnhancement.Enter(player);
        if (player.Hunter == Hunter.Sylux && player.EnhancedState.Flags != 0)
            player.EnhancedState.TimerB = 45;
    }
    internal static void OnExitAlt(PlayerEntity player)
    {
        if (player.Hunter == Hunter.Weavel)
        { player.EnhancedState.ValueB = 0; player.EnhancedState.TimerB = 0; }
    }
    internal static void OnAltAttackStarted(PlayerEntity player)
    {
        if (player.Hunter == Hunter.Trace) player.EnhancedState.GhostFrames = 0;
    }
    internal static void OnAltAttackHit(PlayerEntity attacker, PlayerEntity victim)
    {
        switch (attacker.Hunter)
        {
            case Hunter.Kanden: if (HasMark(attacker, victim)) KandenEnhancement.Overload(attacker, victim); break;
            case Hunter.Trace: TraceEnhancement.AltHit(attacker, victim); break;
            case Hunter.Noxus: NoxusEnhancement.AltHit(attacker, victim); break;
            case Hunter.Weavel: WeavelEnhancement.AltHit(attacker, victim); break;
        }
    }
    internal static void Bonus(PlayerEntity owner, PlayerEntity target, int damage, Vector3 impulse)
    {
        if (!Authority(owner) || !Hostile(owner, target)) return;
        int before = target.Health;
        try
        {
            _bonusDepth++;
            target.TakeDamage(damage, DamageFlags.NoDmgInvuln, impulse, owner);
        }
        finally { _bonusDepth--; }
        EnhancedHunterTelemetry.Damage(owner.Hunter, Math.Max(0, before - target.Health));
        if (before > 0 && target.Health == 0) EnhancedHunterTelemetry.Event(owner.Hunter, "bonus-kills");
    }
}
