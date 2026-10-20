using System;
using System.Buffers;
using MphRead.Formats;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.Entities;

// Adapted from SecondHunt 41452f2 PlayerDroneLaser (MIT). Native offline
// collision/damage owns hits; render submissions consume only cached geometry.
public partial class PlayerEntity
{
    private int _droneLaserFrame = -1, _droneLaserWait, _droneLaserPattern;
    private float _droneLaserDistance;
    private bool _droneLaserVisible;
    private Vector3 _droneLaserOrigin, _droneLaserEnd, _droneLaserUp;
    private long _droneLaserActivation;
    private ulong _droneLaserLastPayment = ulong.MaxValue;
    internal int DroneLaserFrame => _droneLaserFrame;
    internal int DroneLaserProcessedFrame { get; private set; } = -1;
    internal int DroneLaserCooldown => _droneLaserWait;
    internal int DroneLaserPattern => _droneLaserPattern;
    internal float DroneLaserDistance => _droneLaserDistance;
    internal int DroneLasers { get; private set; }
    internal int DroneLaserOpportunities { get; private set; }
    internal int DroneLaserHitAttempts { get; private set; }
    internal int DroneLaserSoundCount { get; private set; }
    internal bool DroneLaserOn => _droneLaserVisible && ScoutDroneEnabled && _health > 0 && IsAltForm
        && _scene.GameState.MatchState == MatchState.InProgress;
    internal Vector3 DroneLaserOrigin => _droneLaserOrigin;
    internal Vector3 DroneLaserEnd => _droneLaserEnd;

    private void ScoutDroneLaserClearRay()
    {
        _droneLaserVisible = false;
        _droneLaserOrigin = _droneLaserEnd = Vector3.Zero;
    }
    private void ScoutDroneLaserReset()
    {
        _droneLaserActivation++;
        _droneLaserFrame = DroneLaserProcessedFrame = -1;
        _droneLaserWait = 0;
        _droneLaserLastPayment = ulong.MaxValue;
        ScoutDroneLaserClearRay();
    }
    private void ScoutDroneLaserEnd()
    {
        if (_droneLaserFrame >= 0) { _droneLaserWait = 600; _droneLaserActivation++; }
        _droneLaserFrame = -1;
        ScoutDroneLaserClearRay();
    }
    private void ScoutDroneLaserStart()
    {
        Vector3 center = Position.AddY(Fixed.ToFloat(Values.AltColYPos));
        Vector3 aim = NormalizeSpatialOr(_gunVec1, new Vector3(_field70, 0, _field74));
        Vector3 end = center + aim * 40;
        var world = new CollisionResult();
        if (CollisionDetection.CheckBetweenPoints(center, end, TestFlags.Beams, _scene, ref world)) end = world.Position;
        float distance = Vector3.Distance(center, end);
        if (ScoutDroneLaserBodyHit(center, end, .5f, out float fraction, out _, out _)) distance *= fraction;
        _droneLaserDistance = Math.Clamp(distance, 4, 40);
        _droneLaserPattern = (int)Rng.GetRandomInt2(4);
        _droneLaserActivation++;
        _droneLaserFrame = 0;
        DroneLaserProcessedFrame = -1;
        ScoutDroneLaserClearRay();
        DroneLasers++;
    }

    // Literal four-pattern source geometry. The public diagnostic snapshot
    // never owns input, collision, RNG, damage, or the next simulation step.
    internal static void ScoutDroneLaserRay(Vector3 center, Vector3 aim, Vector3 facingRight,
        int pattern, float distance, int frame, out Vector3 origin, out Vector3 direction, out Vector3 up)
    {
        aim = NormalizeSpatialOr(aim, Vector3.UnitZ);
        float horizontal = MathF.Sqrt(aim.X * aim.X + aim.Z * aim.Z);
        Vector3 right = horizontal < .0001f ? NormalizeSpatialOr(facingRight, Vector3.UnitX)
            : new Vector3(aim.Z / horizontal, 0, -aim.X / horizontal);
        // A vertical fallback is horizontal, so right and aim remain independent.
        right = NormalizeSpatialOr(new Vector3(right.X, 0, right.Z), Vector3.UnitX);
        up = NormalizeSpatialOr(Vector3.Cross(aim, right), Vector3.UnitY);
        float fromX = pattern is 0 or 1 ? 2.4f : -2.4f;
        float fromY = pattern is 0 or 2 ? -.65f : .65f;
        float t = Math.Clamp((frame - 57) / 60f, 0, 1);
        Vector3 point = center + aim * distance + right * (fromX * (1 - 2 * t)) + up * (fromY * (1 - 2 * t));
        origin = center + aim * .16f - up * .17f;
        direction = NormalizeSpatialOr(point - origin, aim);
    }

    private bool ScoutDroneLaserBodyHit(Vector3 start, Vector3 end, float radius,
        out float fraction, out PlayerEntity? target, out bool half)
    {
        fraction = 1; target = null; half = false;
        if (!SpatialFinite(start) || !SpatialFinite(end) || (end - start).LengthSquared < 1e-8f) return false;
        foreach (var player in _scene.Players.Items)
        {
            if (ReferenceEquals(player, this) || player._health <= 0 || !player.ModIsInPlay
                || player.Flags2.TestFlag(PlayerFlags2.Spectating)
                || !player.LoadFlags.TestFlag(LoadFlags.Active)) continue;
            if (NetHistoricalTrace.Intersect(NetHistoricalTrace.CurrentBody(player), start, end, radius, out var body)
                && body.Distance >= 0 && body.Distance < fraction)
            { fraction = body.Distance; target = player; half = false; }
            if (player.Hunter == Hunter.Weavel && player.Halfturret is { Health: > 0 } turret
                && player.Flags2.TestFlag(PlayerFlags2.Halfturret))
            {
                var hit = new CollisionResult();
                if (CollisionDetection.CheckCylinderOverlapSphere(start, end, turret.Position, radius + .45f, ref hit)
                    && hit.Distance >= 0 && hit.Distance < fraction)
                { fraction = hit.Distance; target = player; half = true; }
            }
        }
        return target != null;
    }

    private void ScoutDroneLaserStep()
    {
        long activation = _droneLaserActivation;
        int frame = _droneLaserFrame;
        DroneLaserProcessedFrame = frame;
        ScoutDroneLaserClearRay();
        bool scheduled = frame is >= 57 and <= 113 && (frame - 57) % 4 == 0;
        if (scheduled) DroneLaserOpportunities++;
        if (frame is >= 57 and < 123)
        {
            Vector3 center = Position.AddY(Fixed.ToFloat(Values.AltColYPos));
            ScoutDroneLaserRay(center, _gunVec1, new Vector3(_field74, 0, -_field70),
                _droneLaserPattern, _droneLaserDistance, frame, out var origin, out var direction, out _droneLaserUp);
            var world = new CollisionResult();
            // The offset origin cannot emerge on the far side of thin geometry.
            bool blockedOrigin = CollisionDetection.CheckBetweenPoints(center, origin, TestFlags.Beams, _scene, ref world);
            Vector3 end;
            if (blockedOrigin) { origin = center; end = world.Position; }
            else
            {
                end = origin + direction * 60;
                world = new CollisionResult();
                if (CollisionDetection.CheckBetweenPoints(origin, end, TestFlags.Beams, _scene, ref world)) end = world.Position;
                if (ScoutDroneLaserBodyHit(origin, end, .1f, out float fraction, out var target, out bool half))
                {
                    end = origin + (end - origin) * fraction;
                    if (scheduled && target != null) ScoutDroneLaserPay(target, half, direction);
                }
            }
            if (activation != _droneLaserActivation || !ScoutDroneEnabled) return;
            if (_scene.GameState.MatchState != MatchState.InProgress) { ScoutDroneLaserEnd(); return; }
            if (SpatialFinite(origin) && SpatialFinite(end))
            { _droneLaserOrigin = origin; _droneLaserEnd = end; _droneLaserVisible = true; }
            if (frame == 57)
            {
                _soundSource.PlaySfx(Metadata.BeamSfx[(int)BeamType.Imperialist, (int)BeamSfx.Shot]);
                DroneLaserSoundCount++;
            }
        }
        if (++_droneLaserFrame >= 134) ScoutDroneLaserEnd();
    }

    private void ScoutDroneLaserPay(PlayerEntity target, bool half, Vector3 direction)
    {
        // Called synchronously only by the actual current-step ray. Validate
        // exact owners and consume this step before native damage callbacks.
        if (!ScoutDroneEnabled || !IsAltForm || _health <= 0 || _frozenTimer > 0
            || IsMorphing || IsUnmorphing || IsBot || _scene.GameState.MatchState != MatchState.InProgress
            || _droneLaserFrame is < 57 or > 113 || (_droneLaserFrame - 57) % 4 != 0
            || _droneLaserLastPayment == _scene.FrameCount || !ReferenceEquals(target._scene, _scene)
            || ReferenceEquals(target, this) || target._health <= 0 || !target.ModIsInPlay
            || target.Flags2.TestFlag(PlayerFlags2.Spectating)
            || (uint)target.SlotIndex >= _scene.Players.Items.Count
            || !ReferenceEquals(_scene.Players.Items[target.SlotIndex], target)) return;
        _droneLaserLastPayment = _scene.FrameCount;
        DroneLaserHitAttempts++;
        DamageFlags flags = DamageFlags.NoDmgInvuln | (half ? DamageFlags.Halfturret : DamageFlags.None);
        uint damage = _doubleDmgTimer == 0 && IsPrimeHunter ? 15u : 10u;
        // The explicit private argument supplies local text only. Native
        // public calls (including nested damage) always enter with false.
        target.TakeDamageCore(damage, flags, direction, this, scoutDroneLaser: true);
    }

    private void DrawScoutDroneLaser()
    {
        if (!DroneLaserOn || _trailModel == null) return;
        Vector3 span = _droneLaserEnd - _droneLaserOrigin;
        if (!SpatialFinite(span) || span.LengthSquared < 1e-6f) return;
        Vector3 direction = NormalizeSpatialOr(span, Vector3.UnitZ);
        Vector3 side = NormalizeSpatialOr(Vector3.Cross(direction, _droneLaserUp), Vector3.UnitX);
        Vector3 up = NormalizeSpatialOr(Vector3.Cross(side, direction), Vector3.UnitY);
        Texture texture = _trailModel.Model.Recolors[0].Textures[0];
        Material material = _trailModel.Model.Materials[0];
        float uvS = (texture.Width - 1 / 16f) / texture.Width, uvT = (texture.Height - 1 / 16f) / texture.Height;
        for (int quad = 0; quad < 2; quad++)
        {
            Vector3 offset = (quad == 0 ? up : side) * .04f;
            var points = ArrayPool<Vector3>.Shared.Rent(8);
            points[0] = Vector3.Zero; points[1] = span - offset;
            points[2] = new Vector3(0, uvT, 0); points[3] = span + offset;
            points[4] = new Vector3(uvS, 0, 0); points[5] = -offset;
            points[6] = new Vector3(uvS, uvT, 0); points[7] = offset;
            _scene.AddRenderItem(RenderItemType.TrailSingle, 1, _scene.GetNextPolygonId(), new Vector3(1, .15f, .1f),
                material.XRepeat, material.YRepeat, material.ScaleS, material.ScaleT,
                Matrix4.CreateTranslation(_droneLaserOrigin), points, _trailBindingId1);
        }
    }
}
