using System;
using MphRead.Mods;
using MphRead.Entities;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Network;
using MphRead.Mods.Replay;
using OpenTK.Mathematics;

namespace MphRead;
public partial class Scene
{
    private Task<ReplayOwnerTrack?>? _ownerPresentation;
    private CancellationTokenSource? _ownerPresentationCancellation;
    internal string ReplayOwnerProvenance { get; private set; } = "Authority reconstructed";
    internal bool OwnerExportPreparedForDraw { get; private set; }
    private void PrepareOwnerPresentationDraw()
    {
        OwnerExportPreparedForDraw = !ReplayVideoExporter.Rendering || ReplayOwnerExport.Ready;
        ReplayOwnerProvenance = "Authority reconstructed";
    }
    internal void BeginOwnerPresentation(string path)
    {
        EndOwnerPresentation();
        if (Headless.Active) return;
        _ownerPresentationCancellation = new();
        var cancellation = _ownerPresentationCancellation.Token;
        _ownerPresentation = ReplayOwnerTrack.LoadAsyncBounded(path, cancellation);
    }
    internal void EndOwnerPresentation()
    {
        _ownerPresentationCancellation?.Cancel(); _ownerPresentationCancellation?.Dispose();
        _ownerPresentationCancellation = null; _ownerPresentation = null;
        ReplayOwnerProvenance = "Authority reconstructed";
    }
    private void ApplyOwnerPresentation(ref Matrix4 view, ref float worldFov, ref float gunFov)
    {
        ReplayOwnerProvenance = "Authority reconstructed";
        if (Services is not ReplaySceneServices services
            || GameState.ThirdPersonCamera || _cameraMode != CameraMode.Player
            || Players.Main.CameraType != CameraType.First) return;
        if (!OwnerExportPreparedForDraw || !ReplayVideoExporter.Rendering && _ownerPresentation?.IsCompletedSuccessfully != true) return;
        var track = ReplayVideoExporter.Rendering ? ReplayOwnerExport.Track : _ownerPresentation!.Result;
        var player = Players.Main;
        if (services.State.Match is not { } match
            || !services.State.TryGetPlayer(player.SlotIndex, out var state) || state.Health == 0) return;
        double frame = services.Session.RecordingPresentationFrame(double.IsFinite(ReplayPresentationFrame)
            ? ReplayPresentationFrame : services.Session.CurrentFrame);
        if (track?.Sample(frame, match.MatchId, match.AuthorityEpoch, player.SlotIndex,
                state.SlotGeneration, state.LifeId, out var pose) != true)
        { ReplayOwnerProvenance = "Partial/legacy · authority reconstructed"; return; }
        view = pose.View; worldFov = pose.WorldFov; gunFov = pose.GunFov;
        player.ModApplyOwnerRenderPose(pose);
        bool visualApplied = player.ModSetOwnerVisual(pose.Visual);
        bool adaptedAspect = pose.Aspect > 0 && Size.Y > 0 && MathF.Abs(pose.Aspect - Size.X / (float)Size.Y) > .001f;
        ReplayOwnerProvenance = visualApplied
            ? adaptedAspect ? "Owner captured · camera/gun/animation/cosmetics; vertical FOV preserved; horizontal coverage adapted"
                : "Owner captured · camera/gun/animation/cosmetics; vertical FOV preserved"
            : adaptedAspect ? "Partial/legacy · owner camera/gun; animation reconstructed; horizontal coverage adapted"
                : "Partial/legacy · owner camera/gun; animation reconstructed";
    }
}
