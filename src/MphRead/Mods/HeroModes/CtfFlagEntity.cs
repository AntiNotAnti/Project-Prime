using System;
using System.Linq;
using MphRead.Formats;
using MphRead.Mods.HeroModes;
using MphRead.Mods.MatchEvents;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.Entities;

public partial class OctolithFlagEntity
{
    private CtfFlagState? _ctfState;
    private ushort _ctfCarrierLife;
    private readonly CollisionResult[] _ctfHits = new CollisionResult[32];
    private readonly CtfFlagModel _ctfFlagModel = new();
    internal CtfFlagState CtfState => _ctfState ??= new CtfFlagState(_data.TeamId, _basePosition + Vector3.UnitY * CtfRules.FlagRadius);
    private bool CtfAuthority => !NetObjectiveSync.IsClient(_scene) && !_scene.Services.IsReplica;

    private bool CtfClearPath(Vector3 from, Vector3 to)
    {
        if (!CtfRules.Finite(from) || !CtfRules.Finite(to)) return false;
        int count = CollisionDetection.CheckSphereBetweenPoints(from, to, .04f, _ctfHits.Length, false,
            TestFlags.None, _scene, _ctfHits);
        for (int i = 0; i < count; i++)
            if (_ctfHits[i].Distance >= 0 && _ctfHits[i].Distance < .99f) return false;
        return true;
    }
    internal bool CanPickupCtf(PlayerEntity player)
    {
        if (_scene.GameState.Mode != GameMode.CaptureTheFlag || player.TeamIndex == _data.TeamId
            || player.Health <= 0 || !player.ModIsInPlay || player.IsAltForm || player.IsMorphing || player.IsUnmorphing
            || player.OctolithFlag != null || _carrier != null) return false;
        Vector3 body = player.Position + Vector3.UnitY * .65f;
        if ((body - Position).LengthSquared > CtfRules.PickupRadius * CtfRules.PickupRadius) return false;
        if (CtfAuthority && player.SlotIndex == CtfState.LastCarrierSlot && CtfState.ThrowerLock > 0) return false;
        return CtfClearPath(body, Position);
    }
    internal bool TryPickupCtf(PlayerEntity player)
    {
        if (!CtfAuthority || !CanPickupCtf(player)) return false;
        if (!CtfState.Pickup(player.SlotIndex, player.TeamIndex, true, false,
                player.Position + Vector3.UnitY * .65f, true)) return false;
        _ctfCarrierLife = NetPlayerLifecycle.Get(player.SlotIndex);
        CopyCtfToNative(); player.ModCancelFlagCarrierFire();
        _scene.GameState.ObjectivePickups[player.SlotIndex]++;
        _scene.GameState.CtzStats.Objective(player.SlotIndex,secondary:true);
        _scene.MatchEvents.Transition(_scene, MatchSemanticEventType.ObjectivePickedUp, player, entity: Id);
        CtfMessage($"{TeamName()} FLAG TAKEN");
        return true;
    }
    internal bool TryThrowCtf(PlayerEntity player, Vector3 direction)
    {
        if (!CtfAuthority || _carrier != player || !player.ModIsInPlay || player.Health <= 0) return false;
        // Throw from the validated body, never from an unchecked cosmetic hand offset behind a wall.
        CtfState.Follow(player.Position + Vector3.UnitY * .8f);
        return DropCtf(direction, true);
    }
    private bool DropCtf(Vector3 direction, bool thrown)
    {
        PlayerEntity? previous = _carrier;
        if (!CtfState.Drop(direction, thrown)) return false;
        if (previous != null)
        {
            _scene.GameState.OctolithDrops[previous.SlotIndex]++;
            _scene.MatchEvents.Transition(_scene, MatchSemanticEventType.ObjectiveDropped, previous, entity: Id);
        }
        CopyCtfToNative(); CtfMessage($"{TeamName()} FLAG {(thrown ? "THROWN" : "DROPPED")}"); return true;
    }
    private void CopyCtfToNative()
    {
        var state = CtfState;
        PlayerEntity? next = state.CarrierSlot >= 0 ? _scene.Players.Items[state.CarrierSlot] : null;
        if (_carrier != next && _carrier?.OctolithFlag == this) _carrier.OctolithFlag = null;
        _carrier = next;
        if (next != null) next.OctolithFlag = this;
        _lastCarrier = state.LastCarrierSlot >= 0 ? _scene.Players.Items[state.LastCarrierSlot] : null;
        Position = state.Position; _atBase = state.Phase == CtfFlagPhase.Home; _grounded = state.Grounded;
        _resetTimer = state.DroppedSeconds; _gravity = state.Velocity.Y;
        ClosestNode = next?.ClosestNode ?? (_atBase ? BaseClosestNode : null);
    }
    private bool ProcessCtf()
    {
        base.Process();
        if (!CtfAuthority)
        {
            if (_carrier != null)
            {
                Position = _carrier.Position + new Vector3(-.35f * _carrier.Field70, .5f, -.35f * _carrier.Field74);
                _carrier.ModCancelFlagCarrierFire();
            }
            return true;
        }
        var state = CtfState;
        if (state.Phase == CtfFlagPhase.Carried)
        {
            PlayerEntity player = _scene.Players.Items[state.CarrierSlot];
            bool lifeMatches = !NetSession.Active || NetPlayerLifecycle.Get(player.SlotIndex) == _ctfCarrierLife;
            if (player.Health <= 0 || !player.ModIsInPlay || player.IsAltForm || player.IsMorphing
                || player.IsUnmorphing || player.TeamIndex == _data.TeamId || !lifeMatches)
                DropCtf(Vector3.UnitZ, false);
            else
            {
                state.Follow(player.Position + new Vector3(-.35f * player.Field70, .5f, -.35f * player.Field74));
                player.ModCancelFlagCarrierFire();
                _scene.GameState.ObjectiveSeconds[player.SlotIndex] += _scene.FrameTime;
            }
        }
        bool defend = false, attack = false;
        if (state.Phase == CtfFlagPhase.Dropped)
        {
            foreach (var player in _scene.GetPlayerEntities())
            {
                if (player.Health <= 0 || !player.ModIsInPlay) continue;
                var body = player.Position + Vector3.UnitY * .65f;
                if ((body - state.Position).LengthSquared > CtfRules.ReturnRadius * CtfRules.ReturnRadius || !CtfClearPath(body, state.Position)) continue;
                if (player.TeamIndex == _data.TeamId) defend = true; else attack = true;
            }
        }
        if (state.Advance(_scene.FrameTime, defend, attack))
        {
            _scene.MatchEvents.Transition(_scene, MatchSemanticEventType.CtfFlagReset, entity: Id);
            foreach(var player in _scene.GetPlayerEntities())
                if(player.Health>0 && player.ModIsInPlay && player.TeamIndex==_data.TeamId
                    && (player.Position+Vector3.UnitY*.65f-Position).LengthSquared<=CtfRules.ReturnRadius*CtfRules.ReturnRadius
                    && CtfClearPath(player.Position+Vector3.UnitY*.65f,Position)) _scene.GameState.CtzStats.Objective(player.SlotIndex);
            CtfMessage($"{TeamName()} FLAG RETURNED");
        }
        if (state.Phase == CtfFlagPhase.Dropped && !state.Grounded)
        {
            Vector3 from = state.Position;
            Vector3 to = state.ProposeMotion(_scene.FrameTime);
            int count = CollisionDetection.CheckSphereBetweenPoints(from, to, CtfRules.FlagRadius,
                _ctfHits.Length, false, TestFlags.None, _scene, _ctfHits);
            float fraction = 1; Vector3 normal = Vector3.Zero; bool collision = false;
            for (int i = 0; i < count; i++)
            {
                var hit = _ctfHits[i];
                if (hit.Distance < 0 || hit.Distance > fraction || Vector3.Dot(to - from, hit.Plane.Xyz) >= 0) continue;
                fraction = hit.Distance; normal = hit.Plane.Xyz; collision = true;
            }
            var safe = collision ? Vector3.Lerp(from, to, MathF.Max(0, fraction - .001f)) + normal * .002f : to;
            state.ResolveMotion(safe, normal, collision);
            // ResolveMotion can itself recover an invalid position. Publish that recovery too,
            // but do not emit reset audio for Capture(), match initialization or snapshots.
            bool recovered = state.Phase == CtfFlagPhase.Home;
            if (!CtfRules.Finite(state.Position) || _scene.Room != null && state.Position.Y < _scene.Room.Meta.KillHeight)
            { state.Reset(); recovered = true; }
            if (recovered)
            {
                _scene.MatchEvents.Transition(_scene, MatchSemanticEventType.CtfFlagReset, entity: Id, value: 1);
                CtfMessage($"{TeamName()} FLAG RESET");
            }
        }
        CopyCtfToNative();
        return true;
    }
    private void CaptureCtf()
    {
        if (!CtfAuthority || _carrier == null || _carrier.Health <= 0 || !_carrier.ModIsInPlay) return;
        PlayerEntity player = _carrier;
        // FlagBaseEntity already checks its volume. Require an actual home flag, not a vacuous missing-entity check.
        OctolithFlagEntity? own = null;
        foreach (var flag in _scene.GetOctolithFlagEntities()) if (flag.Data.TeamId == player.TeamIndex) { own = flag; break; }
        bool atBase = false;
        foreach (var entity in _scene.Entities) if (entity is FlagBaseEntity b && b.Data.TeamId == player.TeamIndex && b.CtfContains(player.Position)) { atBase = true; break; }
        if (!CtfState.Capture(player.SlotIndex, own?.AtBase == true, atBase)) return;
        _scene.GameState.Points[player.SlotIndex]++;
        _scene.GameState.OctolithScores[player.SlotIndex]++;
        _scene.MatchEvents.Transition(_scene, MatchSemanticEventType.ObjectiveCaptured, player, entity: Id);
        CopyCtfToNative(); CtfMessage($"{(player.TeamIndex == 0 ? "RED" : "BLUE")} TEAM CAPTURED THE FLAG");
    }
    private string TeamName() => _data.TeamId == 0 ? "RED" : "BLUE";
    private void CtfMessage(string text)
    {
        if (!Mods.Headless.Active && !_scene.Services.IsReplica) _scene.Players.Main.ModSetCtfEvent(text);
    }
    private void DrawCtfFlag()
    {
        if (_scene.GameState.Mode != GameMode.CaptureTheFlag) { base.GetDrawInfo(); return; }
        if (_carrier?.IsMainPlayer == true && !_carrier.Flags2.TestFlag(PlayerFlags2.DrawnThirdPerson)) return;
        _ctfFlagModel.Draw(_scene, Position - Vector3.UnitY * CtfRules.FlagRadius,
            Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, 1f, _data.TeamId);
    }
}
