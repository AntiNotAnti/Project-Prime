#if !ANDROID && !MPHREAD_SERVER
using System;
using System.Reflection;
using MphRead.Entities;
using MphRead.Mods.Input;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.Mods.Render;

/// <summary>Runs with the renderer probe's native graphics context, without game assets.</summary>
internal static class StudioReplayCameraCheck
{
    internal static void Verify()
    {
        var scene = new Scene(new Vector2i(256, 192), SyntheticInput.CreateKeyboard(),
            SyntheticInput.CreateMouse(), _ => { }, () => { },
            new ReplaySceneServices(null!, new ReplayReplicaState()), initializeRuntime: false);
        // No world shader is attached to this synthetic scene. Native GL/WGPU
        // accepts -1 for an inactive uniform while exercising camera extraction.
        ((ShaderLocations)typeof(Scene).GetField("_shaderLocations",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(scene)!).ViewMatrix = -1;
        var player = scene.Players.Main;
        typeof(PlayerEntity).GetProperty(nameof(PlayerEntity.Values))!
            .SetValue(player, Metadata.PlayerValues[(int)Hunter.Samus]);
        void Set(string name, Vector3 value) => typeof(PlayerEntity)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(player, value);
        void Pose(Vector3 direction, Vector3 gunOffset)
        {
            Set("_gunVec1", direction); Set("_aimVec", direction); Set("_upVector", Vector3.UnitY);
            Set("_gunDrawPos", Vector3.TransformVector(gunOffset, EntityBase.GetTransformMatrix(direction, Vector3.UnitY)));
            player.CameraInfo.Position = Vector3.Zero; player.CameraInfo.Target = direction;
            player.CameraInfo.UpVector = Vector3.UnitY; player.CameraInfo.Fov = 78;
            player.CameraInfo.Update(); player.CameraInfo.ModCaptureDrawState(); player.ModCaptureFirstPersonDrawState();
        }
        Pose(-Vector3.UnitZ, new(.2f, -.15f, .5f));
        player.CameraInfo.ModResetDrawState(); player.ModResetFirstPersonDrawState();
        Pose(new Vector3(.2f, .1f, -1).Normalized(), new(.3f, -.1f, .6f));
        Matrix4 Draw(double alpha)
        {
            scene.ReplayRenderAlpha = (float)alpha;
            scene.SetStudioReplayPlayerCamera(0);
            // This invalidation used to discard Studio's only prepared pose.
            player.ModInvalidateFirstPersonRenderPose();
            scene.PrepareDrawCamera();
            if (!player.ModGetFirstPersonGunTransform(out var gun))
                throw new InvalidOperationException("Studio draw did not rebuild its fractional cannon pose.");
            return gun;
        }
        var start = Draw(0); var middle = Draw(.5); var end = Draw(1);
        if (start == middle || middle == end || start == end)
            throw new InvalidOperationException("Studio cannon snaps instead of following the fractional replay camera.");
        if (middle != Draw(.5))
            throw new InvalidOperationException("Studio cannon changes when the same presentation frame is redrawn.");
        // An authored camera must survive the same extraction boundary.
        scene.SetStudioReplayCamera(new(2, 3, 4), Vector3.Zero, Vector3.UnitY, 65);
        player.ModInvalidateFirstPersonRenderPose(); scene.PrepareDrawCamera();
        if (scene.CameraMode == CameraMode.Player || player.ModGetFirstPersonGunTransform(out _))
            throw new InvalidOperationException("Studio authored camera inherited a player cannon pose.");
        Console.WriteLine("[renderwindowcheck] Studio draw rebuilds fractional cannon poses and preserves authored cameras PASS");
    }
}
#endif
