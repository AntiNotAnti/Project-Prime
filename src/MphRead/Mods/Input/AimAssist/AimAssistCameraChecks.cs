using System;
using System.Reflection;
using MphRead.Entities;
using OpenTK.Mathematics;

namespace MphRead.Mods.Input.AimAssist
{
    // Exercise the real camera mutators as well as the allocation-free assist core.
    internal static class AimAssistCameraChecks
    {
        internal static void Run()
        {
            var priorGame = GameState.Current;
            var priorPlayers = PlayerEntity.LegacyRegistry;
            var priorRandom = Rng.Current;
            try
            {
                var scene = new Scene(new Vector2i(256, 192), SyntheticInput.CreateKeyboard(),
                    SyntheticInput.CreateMouse(), _ => { }, () => { }, initializeRuntime: false);
                var player = scene.Players.Main;
                typeof(PlayerEntity).GetProperty(nameof(PlayerEntity.Values))!
                    .SetValue(player, Metadata.PlayerValues[(int)Hunter.Samus]);
                const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
                FieldInfo gun = typeof(PlayerEntity).GetField("_gunVec1", flags)!;
                FieldInfo facing = typeof(PlayerEntity).GetField("_facingVector", flags)!;
                FieldInfo pitch = typeof(PlayerEntity).GetField("_aimY", flags)!;
                MethodInfo yawInput = typeof(PlayerEntity).GetMethod("UpdateAimX", flags)!;
                MethodInfo pitchInput = typeof(PlayerEntity).GetMethod("UpdateAimY", flags)!;
                MethodInfo updateFacing = typeof(PlayerEntity).GetMethod("UpdateAimFacing", flags)!;
                void Reset()
                {
                    gun.SetValue(player, Vector3.UnitZ);
                    pitch.SetValue(player, 0f);
                    facing.SetValue(player, Vector3.UnitZ);
                }
                void Near(float actual, float expected, string name)
                    => GamepadChecks.Check(Math.Abs(actual - expected) < .001f, "aim camera: " + name);
                float Yaw()
                {
                    var direction = (Vector3)gun.GetValue(player)!;
                    return MathHelper.RadiansToDegrees(MathF.Atan2(direction.X, direction.Z));
                }

                Reset();
                float height = Fixed.ToFloat(player.Values.MaxPickupHeight);
                player.CameraInfo.Position = player.Position + new Vector3(0, height - .15f, -10);
                MethodInfo project = typeof(PlayerEntity).GetMethod("AssistRegion", flags)!;
                var headRegion = (AimAssistRegion)project.Invoke(player, new object[] { player, height - .3f, height, .5f })!;
                GamepadChecks.Check(AimAssistMath.InsideRegion(headRegion), "aim camera: projected head center is valid");
                Near(headRegion.MaxYaw, MathHelper.RadiansToDegrees(MathF.Asin(.5f / 10)),
                    "head width derives from collision radius");
                Near(headRegion.MinPitch, -MathHelper.RadiansToDegrees(MathF.Atan2(.15f, 9.5f)),
                    "lower head bound projects mechanical band");
                Near(headRegion.MaxPitch, MathHelper.RadiansToDegrees(MathF.Atan2(.15f, 9.5f)),
                    "upper head bound projects mechanical band");

                player.EquipInfo.Zoomed = true;
                player.CameraInfo.Fov = Fixed.ToFloat(player.Values.NormalFov) * 2 * .25f;
                Reset();
                yawInput.Invoke(player, new object[] { 4f, true });
                Near(Yaw(), 1, "ordinary input retains zoom sensitivity");
                Reset();
                yawInput.Invoke(player, new object[] { 4f, false });
                Near(Yaw(), 4, "assisted yaw is not scaled by zoom a second time");
                Reset();
                pitchInput.Invoke(player, new object[] { 4f, false });
                var direction = (Vector3)gun.GetValue(player)!;
                Near(MathHelper.RadiansToDegrees(MathF.Asin(direction.Y)), 4,
                    "assisted pitch is in the same angular units as target errors");
                Reset();
                pitchInput.Invoke(player, new object[] { 90f, false });
                Near((float)pitch.GetValue(player)!, 85, "assistance still obeys the camera pitch limit");

                // Exact opposition used to normalize a zero tangent in
                // UpdateAimFacing and poison facing/movement/muzzle state with
                // NaNs. The camera path must choose a stable basis instead.
                gun.SetValue(player, Vector3.UnitZ);
                facing.SetValue(player, -Vector3.UnitZ);
                updateFacing.Invoke(player, Array.Empty<object>());
                var recoveredFacing = (Vector3)facing.GetValue(player)!;
                GamepadChecks.Check(Single.IsFinite(recoveredFacing.X)
                    && Single.IsFinite(recoveredFacing.Y)
                    && Single.IsFinite(recoveredFacing.Z)
                    && recoveredFacing.LengthSquared > .99f,
                    "aim camera: opposite aim/body vectors recover a finite facing basis");

                // A poisoned gun vector must self-heal from the body basis on
                // the next aim update rather than becoming a permanent state.
                gun.SetValue(player, new Vector3(Single.NaN, 0, 0));
                facing.SetValue(player, Vector3.UnitZ);
                yawInput.Invoke(player, new object[] { 1f, false });
                var recoveredGun = (Vector3)gun.GetValue(player)!;
                GamepadChecks.Check(Single.IsFinite(recoveredGun.X)
                    && Single.IsFinite(recoveredGun.Y)
                    && Single.IsFinite(recoveredGun.Z)
                    && recoveredGun.LengthSquared > .99f,
                    "aim camera: non-finite gun vector self-heals before input");
            }
            finally
            {
                GameState.Current = priorGame;
                PlayerEntity.LegacyRegistry = priorPlayers;
                Rng.Current = priorRandom;
            }
        }
    }
}
