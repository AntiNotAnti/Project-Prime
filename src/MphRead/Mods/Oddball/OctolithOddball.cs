using System;
using MphRead.Formats;
using MphRead.Mods;
using MphRead.Mods.Oddball;
using OpenTK.Mathematics;

namespace MphRead.Entities;

public partial class OctolithFlagEntity
{
    private readonly int[] _oddPickupCooldown = new int[8];
    private readonly CollisionResult[] _oddCollision = new CollisionResult[16];
    private readonly OddballSkullModel _oddSkull = new();
    private int _oddLooseTicks;

    public bool OddballTryPickup(PlayerEntity player, bool inputRequired = true)
    {
        if (MphRead.Mods.Network.NetObjectiveSync.IsClient(_scene) || _scene.Services.IsReplica) return false;
        if (_scene.GameState.Mode != GameMode.Oddball || _scene.GameState.Oddball?.CanPickUp != true
            || _carrier != null || (uint)player.SlotIndex >= 8 || (uint)player.TeamIndex >= 2
            || _oddPickupCooldown[player.SlotIndex] > 0 || player.Health <= 0 || player.IsAltForm || player.IsMorphing
            || !player.LoadFlags.TestFlag(LoadFlags.Active)
            || (inputRequired && !player.IsBot && !player.Controls.Zoom.IsPressed)) return false;
        Vector3 center = player.Position.AddY(1);
        if ((Position - center).LengthSquared > 2.1f * 2.1f) return false;
        CollisionResult hit = default;
        if (CollisionDetection.CheckBetweenPoints(center, Position, TestFlags.None, _scene, ref hit)) return false;
        if (!OnTouched(player)) return false;
        player.ModOddballPickedUp();
        _oddLooseTicks = 0;
        _scene.GameState.Oddball?.Present(_scene, $"BALL TAKEN - {(player.TeamIndex == 0 ? "RED" : "BLUE")}", "ball_taken");
        if (!Headless.Active && !OddballAudio.HasCue(_scene, "ball_taken")) _soundSource.PlayFreeSfx(SfxId.FLAG_ACQUIRED);
        return true;
    }

    public void OddballDrop()
    {
        if (MphRead.Mods.Network.NetObjectiveSync.IsClient(_scene) || _scene.Services.IsReplica) return;
        if (_carrier == null) return;
        var player = _carrier;
        Position = player.Position.AddY(1.05f);
        _scene.GameState.OctolithDrops[player.SlotIndex]++;
        _scene.MatchEvents.Transition(_scene, Mods.MatchEvents.MatchSemanticEventType.ObjectiveDropped, player, entity: Id);
        _oddPickupCooldown[player.SlotIndex] = 30;
        player.OctolithFlag = null; _carrier = null;
        _grounded = false; _atBase = false; _resetTimer = 0; _gravity = 0; _oddLooseTicks = 0;
        _scene.GameState.Oddball?.Present(_scene, "BALL DROPPED");
        if (!Headless.Active) _soundSource.PlayFreeSfx(SfxId.FLAG_DROPPED);
    }

    internal void OddballSetBase(Vector3 position)
    {
        if (_scene.GameState.Mode != GameMode.Oddball) return;
        _basePosition = position;
        BaseClosestNode = _scene.Room?.NodeData is {} nodes ? ReadNodeData.FindClosestNode(nodes, position) : null;
        OddballReset();
    }

    public void OddballReset()
    {
        SetAtBase(); _oddLooseTicks = 0; Array.Clear(_oddPickupCooldown);
    }

    private bool ProcessOddball()
    {
        base.Process();
        if(MphRead.Mods.Network.NetObjectiveSync.IsClient(_scene) || _scene.Services.IsReplica) { if(_carrier!=null)Position=_carrier.Position.AddY(1.05f)+_carrier.ModGunVector*.48f; return true; }
        for (int i = 0; i < 8; i++) if (_oddPickupCooldown[i] > 0) _oddPickupCooldown[i]--;
        var match = _scene.GameState.Oddball;
        if (match == null || !match.CanPickUp) return true;
        if (_carrier != null)
        {
            if (_carrier.Health == 0 || _carrier.IsAltForm || _carrier.IsMorphing
                || !_carrier.LoadFlags.TestFlag(LoadFlags.Active)) OddballDrop();
            else
            {
                Vector3 facing = _carrier.ModGunVector;
                facing.Y = 0;
                if (facing.LengthSquared > .001f) facing.Normalize(); else facing = Vector3.UnitZ;
                Position = _carrier.Position.AddY(1.05f) + facing * .48f;
                ClosestNode = _carrier.ClosestNode;
                return true;
            }
        }
        if (!_grounded)
        {
            Vector3 prev = Position;
            (float velocity, float displacement) = ConstantAcceleration(-.02f, _gravity);
            _gravity = velocity; Position = Position.AddY(displacement);
            int count = CollisionDetection.CheckSphereBetweenPoints(prev, Position, 1.25f,
                16, false, TestFlags.None, _scene, _oddCollision);
            for (int i = 0; i < count; i++)
            {
                var hit = _oddCollision[i];
                if (hit.Plane.Y > .342f)
                {
                    float dist = hit.Plane.W - Vector3.Dot(Position.AddY(-1.25f), hit.Plane.Xyz);
                    Position += hit.Plane.Xyz * dist;
                    _grounded = true; _gravity = 0;
                }
            }
        }
        if (!Single.IsFinite(Position.X) || !Single.IsFinite(Position.Y) || !Single.IsFinite(Position.Z)
            || Position.Y < (_scene.Room?.Meta.KillHeight ?? -100) || (!_atBase && ++_oddLooseTicks >= 20 * 60))
        {
            OddballReset();
            _scene.GameState.Oddball?.Present(_scene, "BALL RESET", "play_ball");
            if (!Headless.Active && !OddballAudio.HasCue(_scene, "play_ball")) _soundSource.PlayFreeSfx(SfxId.FLAG_RESET2);
        }
        foreach (var player in _scene.GetPlayerEntities()) if (OddballTryPickup(player)) break;
        return true;
    }

    public override void GetDrawInfo()
    {
        if (_scene.GameState.Mode == GameMode.CaptureTheFlag) { DrawCtfFlag(); return; }
        if (_scene.GameState.Mode != GameMode.Oddball) { base.GetDrawInfo(); return; }
        if (_scene.GameState.Oddball?.CanPickUp != true) return;
        if (_carrier?.IsMainPlayer == true && !_carrier.IsAltForm
            && !_carrier.Flags2.TestFlag(PlayerFlags2.DrawnThirdPerson)) return;
        Vector3 forward = _carrier?.ModGunVector ?? new Vector3(MathF.Sin(_scene.ElapsedTime), 0, MathF.Cos(_scene.ElapsedTime));
        forward.Y = 0; if (forward.LengthSquared < .001f) forward = Vector3.UnitZ; else forward.Normalize();
        Vector3 right = Vector3.Cross(Vector3.UnitY, forward).Normalized();
        _oddSkull.Draw(_scene, Position, right, Vector3.UnitY, forward, .9f);
    }
}
