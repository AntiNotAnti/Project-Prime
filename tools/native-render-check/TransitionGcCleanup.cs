using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using MphRead;
using MphRead.Entities;
using OpenTK.Graphics.OpenGL;

sealed partial class CheckWindow
{
    private sealed record TransitionHandles(WeakReference<Scene> Scene, int[] Programs, int[] Textures, int[] Framebuffers);

    // The ordinary ownership checker intentionally keeps Scene alive to inspect
    // cleared fields. Residency controls must let the normal EndScene collection
    // see the normal reachability, so no scene local crosses this call boundary.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private TransitionHandles SnapshotTransitionHandles()
    {
        Scene scene = Scene;
        int Field(string name) => (int)typeof(Scene).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(scene)!;
        var textures = (System.Collections.Generic.HashSet<int>)typeof(Scene)
            .GetField("_ownedTextures", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(scene)!;
        return new(new WeakReference<Scene>(scene),
            new[] { "_shaderProgramId", "_rttShaderProgramId", "_shiftShaderProgramId", "_celShaderProgramId" }
                .Select(Field).Where(id => id != 0).ToArray(), textures.ToArray(),
            new[] { "_frameBuffer", "_shadowFramebuffer" }.Select(Field).Where(id => id != 0).ToArray());
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool SceneTargetAvailable(WeakReference<Scene> scene) => scene.TryGetTarget(out _);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void RebindNextForeground()
    {
        BeginScene();
        PlayerEntity.MaxPlayers = 1;
        AddPlayer(Hunter.Samus);
        PlayerEntity.PlayerCount = 1;
        PlayerEntity.MainPlayerIndex = 0;
        AddRoom(_room, GameMode.Battle, playerCount: 1);
        LoadScene();
    }

    internal object CleanupTransitionGcAndInspect(bool rebind = false)
    {
        TransitionHandles handles = SnapshotTransitionHandles();
        EndScene();
        if (handles.Programs.Any(GL.IsProgram) || handles.Textures.Any(GL.IsTexture)
            || handles.Framebuffers.Any(GL.IsFramebuffer) || GL.GetError() != ErrorCode.NoError)
            throw new InvalidOperationException("Transition GC cleanup retained recognized owned GL handles or an error.");
        // Sampling this weak target is after the existing collection; never use
        // target survival as a requirement or request an extra collection.
        bool sceneReachableAfter = SceneTargetAvailable(handles.Scene);
        bool? sceneAvailableAfterNextLoad = null, sceneAvailableAfterNextEnd = null;
        object? nextForegroundCleanup = null;
        if (rebind)
        {
            RebindNextForeground();
            sceneAvailableAfterNextLoad = SceneTargetAvailable(handles.Scene);
            nextForegroundCleanup = CleanupTransitionGcAndInspect();
            sceneAvailableAfterNextEnd = SceneTargetAvailable(handles.Scene);
        }
        return new { ownedProgramsBefore = handles.Programs.Length, recognizedProgramsAfter = 0,
            ownedTexturesBefore = handles.Textures.Length, recognizedTexturesAfter = 0,
            ownedFramebuffersBefore = handles.Framebuffers.Length, recognizedFramebuffersAfter = 0,
            sceneWeakTargetAvailableAfter = sceneReachableAfter,
            sceneAvailableAfterNextLoad, sceneAvailableAfterNextEnd, nextForegroundCleanup,
            scope = "GC diagnostic cleanup: only weak scene reference and numeric handles cross EndScene; no extra GC or finalizer drain. Ordinary ownership-field inspection is a separate check." };
    }
}
