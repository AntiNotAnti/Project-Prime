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
    private ReplayOwnerPresentationSource? _ownedPresentationSource;
    internal string ReplayOwnerProvenance { get; private set; } = "Authority reconstructed";
    internal bool OwnerExportPreparedForDraw { get; private set; }
    internal float ReplayCustomScopeBlend(PlayerEntity player)
    {
        if (!TryReplayCustomPresentation(out var services, out _) || !CustomWeaponHud(player).Visible) return 0;
        var track = ReplayVideoExporter.Rendering ? ReplayOwnerExport.Track
            : _ownerPresentation?.IsCompletedSuccessfully == true ? _ownerPresentation.Result : null;
        if (OwnerExportPreparedForDraw && services.State.Match is { } match
            && services.State.TryGetPlayer(player.SlotIndex, out var state) && state.Health > 0)
        {
            double frame = services.Session.RecordingPresentationFrame(double.IsFinite(ReplayPresentationFrame)
                ? ReplayPresentationFrame : services.Session.CurrentFrame);
            if (track?.Sample(frame, match.MatchId, match.AuthorityEpoch, player.SlotIndex,
                state.SlotGeneration, state.LifeId, out var pose) == true && pose.CustomScopeBlend is { } blend) return blend;
        }
        return CustomWeaponHud(player).State.Optic > 0 ? 1 : 0;
    }
    private void PrepareOwnerPresentationDraw()
    {
        OwnerExportPreparedForDraw = !ReplayVideoExporter.Rendering || ReplayOwnerExport.Ready;
        ReplayOwnerProvenance = "Authority reconstructed";
    }
    internal void BeginOwnerPresentation(string path, ReplayOwnerPresentationSource? shared = null)
    {
        EndOwnerPresentation();
        if (Headless.Active) return;
        if (shared == null) shared = _ownedPresentationSource = new(path);
        _ownerPresentation = shared.Load;
    }
    internal void EndOwnerPresentation()
    {
        _ownedPresentationSource?.Dispose(); _ownedPresentationSource = null; _ownerPresentation = null;
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
        ReplayOwnerProvenance = track!.Provenance(visualApplied, adaptedAspect);
    }
}
