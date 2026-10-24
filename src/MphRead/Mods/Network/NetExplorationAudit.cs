using System;
using MphRead.Entities;
using MphRead.Formats;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network;

public static partial class NetTestScript
{
    private static readonly bool ExplorationRequested =
        Environment.GetEnvironmentVariable("PROJECT_PRIME_EXPLORATION_AUDIT") == "1";
    internal static bool GroundedExploration => ExplorationRequested && OfflineInputScene != null;
    private static int _explorationBlockedMoves;
    internal static int ExplorationBlockedMoves => _explorationBlockedMoves;

    // A walking acceptance tour, separate from the combat/stress tour whose
    // jumps, boosts and fixed square can deliberately leave an open rooftop.
    // Only ordinary controls are written. Collision, health, position and
    // velocity remain entirely under the normal player controller.
    private static void DriveExploration(PlayerEntity player, PlayerControls controls)
    {
        AimDeltaX = AimDeltaY = 0;
        controls.ClearAnalogMovement();
        if (_frame % 10 == 0)
            Console.WriteLine($"EXPLORETRACE frame={_frame} position={player.Position} speed={player.Speed} alt={player.IsAltForm} settled={Settled(player)}");
        bool wantAlt = _frame >= 240 && _frame < 420;
        bool changingForm = player.IsAltForm != wantAlt;
        Hold(controls.Morph, Settled(player) && changingForm
            && player.Speed.Xz.LengthSquared < .0001f && _frame % 12 == 0);
        Hold(controls.Shoot, !player.IsAltForm && _frame % 90 < 12);
        if (!Settled(player)) return;
        if (_frame < 60 || _frame >= 1050 || changingForm)
        {
            ExplorationBrake(player, controls);
            return;
        }
        Vector3 momentum = player.Speed.WithY(0);
        if (momentum.LengthSquared > .0009f
            && !ExplorationCanWalk(player, momentum.Normalized(), Math.Max(1.5f, momentum.Length * 8)))
        {
            ExplorationBrake(player, controls);
            return;
        }

        player.GetExplorationMovementBasis(out Vector3 forward, out Vector3 right);
        if (forward.LengthSquared < .001f || right.LengthSquared < .001f) return;
        forward.Normalize();
        right.Normalize();
        var directions = new[] { forward, (forward+right).Normalized(), right,
            (right-forward).Normalized(), -forward, (-forward-right).Normalized(),
            -right, (forward-right).Normalized() };
        int preferred = _frame / 90 % 8;
        for (int offset = 0; offset < 8; offset++)
        {
            int choice = (preferred + offset) % 8;
            if (ExplorationCanWalk(player, directions[choice])
                && ExplorationCameraCanWalk(player, choice))
            {
                if (_frame % 10 == 0) Console.WriteLine($"EXPLORETRACE choice={choice} direction={directions[choice]}");
                bool rolling = player.IsAltForm && player.Values.AltFormStrafe == 0;
                Hold(rolling ? controls.RollUp : controls.MoveUp, choice is 0 or 1 or 7);
                Hold(rolling ? controls.RollRight : controls.MoveRight, choice is 1 or 2 or 3);
                Hold(rolling ? controls.RollDown : controls.MoveDown, choice is 3 or 4 or 5);
                Hold(rolling ? controls.RolltLeft : controls.MoveLeft, choice is 5 or 6 or 7);
                return;
            }
            _explorationBlockedMoves++;
        }
    }

    private static void ExplorationBrake(PlayerEntity player, PlayerControls controls)
    {
        Vector3 speed = player.Speed.WithY(0);
        if (speed.LengthSquared < .0001f) return;
        player.GetExplorationMovementBasis(out Vector3 forward, out Vector3 right);
        if (forward.LengthSquared < .001f || right.LengthSquared < .001f) return;
        forward.Normalize(); right.Normalize();
        // Releasing roll input coasts for many metres. Countersteer using
        // ordinary analogue input, easing the request as speed approaches zero.
        bool rolling = player.IsAltForm && player.Values.AltFormStrafe == 0;
        if (rolling && player.CameraInfo.Field48 * player.CameraInfo.Field48
            + player.CameraInfo.Field4C * player.CameraInfo.Field4C > .001f)
        {
            // Countersteering changes input direction and releases a retained
            // rolling basis. Use the camera basis the controller will refresh.
            forward = new Vector3(player.CameraInfo.Field48, 0, player.CameraInfo.Field4C).Normalized();
            right = new Vector3(-player.CameraInfo.Field50, 0, -player.CameraInfo.Field54).Normalized();
        }
        float x = Math.Clamp(-Vector3.Dot(speed, right) / .25f, -1, 1);
        float y = Math.Clamp(-Vector3.Dot(speed, forward) / .25f, -1, 1);
        controls.SetAnalogMovement(x, y);
        Hold(rolling ? controls.RollUp : controls.MoveUp, y > 0);
        Hold(rolling ? controls.RollDown : controls.MoveDown, y < 0);
        Hold(rolling ? controls.RollRight : controls.MoveRight, x > 0);
        Hold(rolling ? controls.RolltLeft : controls.MoveLeft, x < 0);
        if (_frame % 10 == 0) Console.WriteLine($"EXPLORETRACE braking x={x} y={y}");
    }

    private static bool ExplorationCameraCanWalk(PlayerEntity player, int choice)
    {
        if (!player.IsAltForm || player.Values.AltFormStrafe != 0) return true;
        // Rolling can refresh its basis from the camera after input changes.
        // Require both the retained and prospective camera basis to be safe.
        Vector3 forward = new(player.CameraInfo.Field48, 0, player.CameraInfo.Field4C);
        Vector3 right = new(-player.CameraInfo.Field50, 0, -player.CameraInfo.Field54);
        Vector3 direction = choice switch
        {
            0 => forward, 1 => forward + right, 2 => right, 3 => right - forward,
            4 => -forward, 5 => -forward - right, 6 => -right, _ => forward - right
        };
        return direction.LengthSquared > .001f && ExplorationCanWalk(player, direction.Normalized());
    }

    private static bool ExplorationCanWalk(PlayerEntity player, Vector3 direction, float reach = 1.5f)
    {
        Scene scene = OfflineInputScene!;
        float radius = player.IsAltForm ? Fixed.ToFloat(player.Values.AltColRadius) : .5f;
        float feetOffset = player.IsAltForm
            ? Fixed.ToFloat(player.Values.AltColYPos - player.Values.AltColRadius)
            : Fixed.ToFloat(player.Values.MinPickupHeight);
        Vector3 feet = player.Position + Vector3.UnitY * feetOffset;
        foreach (float height in new[] { radius, player.IsAltForm ? radius : 1.5f })
        {
            Vector3 start = feet + Vector3.UnitY * height;
            CollisionResult wall = default;
            if (CollisionDetection.CheckBetweenPoints(start, start + direction * reach,
                TestFlags.None, scene, ref wall)) return false;
        }
        Vector3 side = new Vector3(direction.Z, 0, -direction.X) * (radius + .1f);
        foreach (float distance in new[] { .5f, 1f, reach })
        foreach (float lateral in new[] { -1f, 0f, 1f })
        {
            Vector3 point = feet + direction * distance + side * lateral;
            CollisionResult floor = default;
            if (!CollisionDetection.CheckBetweenPoints(point + Vector3.UnitY * .75f,
                point - Vector3.UnitY * 1.25f, TestFlags.None, scene, ref floor)
                || floor.Plane.Y < .7f || (int)floor.Terrain >= (int)Terrain.Lava
                || floor.Position.Y < feet.Y - .35f || floor.Position.Y > feet.Y + .35f)
                return false;
        }
        return true;
    }
}
