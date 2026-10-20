using System;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods.Network;
using MphRead.Mods.Replay;
using OpenTK.Mathematics;

namespace MphRead;
public partial class Scene
{
    private ReplayDirectorShotKind? _directorShot;
    private Vector3? _directorFacing, _directorTarget;
    private ushort _directorLife, _directorGeneration;
    private Func<Vector3, Vector3, bool>? _directorClearance;
    private int _directorPictureSubject = -1;
    internal string ReplayDirectorShotDescription { get; private set; } = "Canonical POV";
    private void ResetDirectorShot()
    { _directorShot = null; _directorFacing = null; _directorTarget = null; _replayFollowPosition = null; }
    private DirectorSubjectScope BeginDirectorPicturePreparation()
    { _directorPictureSubject = -1; return new(this, -1); }
    private DirectorSubjectScope BeginDirectorPictureRender() => new(this, _directorPictureSubject);
    private readonly struct DirectorSubjectScope : IDisposable
    {
        private readonly Scene _scene; private readonly int _previous;
        internal DirectorSubjectScope(Scene scene, int subject)
        {
            _scene = scene; _previous = scene.Players.MainPlayerIndex;
            if (scene.Services.IsReplica && subject >= 0 && subject < scene.Players.Items.Count)
                scene.Players.MainPlayerIndex = subject;
        }
        public void Dispose() => _scene.Players.MainPlayerIndex = _previous;
    }
    private bool DirectorSegmentClear(Vector3 from, Vector3 to)
    {
        CollisionResult collision = default;
        return !CollisionDetection.CheckBetweenPoints(from, to, TestFlags.Players, this, ref collision);
    }
    private void ApplyDirectorShot(ReplayCameraMode style, double deltaFrames)
    {
        var player = Players.Main;
        if (Services is not ReplaySceneServices replica || !replica.State.TryGetPlayer(player.SlotIndex, out var state)
            || state.Health == 0 || !player.LoadFlags.TestFlag(LoadFlags.Spawned) || player.IsMorphing || player.IsUnmorphing)
        { SetFreeCamera(false); ResetDirectorShot(); ReplayDirectorShotDescription = "Canonical POV · transition/unavailable"; return; }
        var position = player.ReplayDrawTransform.Row3.Xyz;
        if (_directorGeneration != state.SlotGeneration || _directorLife != state.LifeId
            || _directorTarget is { } old && (old - position).LengthSquared > 64)
        { ResetDirectorShot(); deltaFrames = 0; }
        _directorGeneration = state.SlotGeneration; _directorLife = state.LifeId; _directorTarget = position;
        bool exact = ReplayVideoExporter.Rendering;
        var facing = player.CameraInfo.Facing;
        if (ReplayPoses?.SamplePresented(player.SlotIndex, ReplayRenderAlpha, out _, out var sampled) == true) facing = sampled;
        Vector3 target = position + Vector3.UnitY * Math.Clamp(ReplayCamera.Height, .5f, 8);
        ReplayDirectorPov? victim = null;
        int victimIndex = -1;
        foreach (var fact in replica.State.SemanticEvents.Events)
        {
            if (exact || fact.Type != Mods.MatchEvents.MatchSemanticEventType.Headshot
                || fact.ActorSlot != player.SlotIndex || fact.ActorGeneration != state.SlotGeneration
                || fact.ActorLife != state.LifeId || fact.Tick > replica.State.ServerTick
                || replica.State.ServerTick - fact.Tick > 120 || fact.TargetSlot >= 8
                || !replica.State.TryGetPlayer(fact.TargetSlot, out var targetState) || targetState.Health == 0
                || targetState.SlotGeneration != fact.TargetGeneration || targetState.LifeId != fact.TargetLife) continue;
            for (int i = 0; i < Players.Items.Count; i++)
            {
                var other = Players.Items[i];
                if (other.SlotIndex == fact.TargetSlot && other.CameraType == CameraType.First)
                { victim = new(other.CameraInfo.Position, other.CameraInfo.Facing); victimIndex = i; }
            }
        }
        // An authored key is a genuine fixed location. Without one, do not
        // fabricate an arena camera from an arbitrary player's current position.
        Vector3? fixedPosition = !exact && ReplayCamera.Track.Keys.Count > 0 ? ReplayCamera.Track.Keys[0].Position : null;
        Span<ReplayDirectorShot> candidates = stackalloc ReplayDirectorShot[ReplayDirectorShots.MaximumCandidates];
        // Exact export uses canonical world-axis chase/tactical offsets (zero
        // angular drift) and the canonical 0.4rad/s orbit. It never depends on
        // how many preview pictures preceded this frame. Subject/shot changes
        // and seeks are explicit cuts, rather than smoothed across identities.
        int count = ReplayDirectorShots.Generate(candidates, player.CameraInfo.Position, exact ? -Vector3.UnitZ : facing, target,
            ReplayPresentationTime, style, ReplayCamera.FieldOfView, exact ? null : _directorShot,
            _directorClearance ??= DirectorSegmentClear, victim, fixedPosition, preferVictim: victimIndex >= 0);
        if (count == 0) { SetFreeCamera(false); ResetDirectorShot(); return; }
        var best = candidates[0];
        for (int i = 1; i < count; i++) if (candidates[i].Score > best.Score) best = candidates[i];
        if (best.Kind == ReplayDirectorShotKind.KillerPov)
        { SetFreeCamera(false); ResetDirectorShot(); ReplayDirectorShotDescription = "Canonical POV · no clear cinematic shot"; return; }
        if (best.Kind == ReplayDirectorShotKind.VictimPov && victimIndex >= 0)
        {
            // The existing render-only subject owner supplies the actual
            // victim camera, weapon and HUD. Both prepare/draw scopes restore
            // the editor's watched player before any simulation can run.
            _directorPictureSubject = victimIndex; Players.MainPlayerIndex = victimIndex;
            SetFreeCamera(false); ResetDirectorShot(); ReplayDirectorShotDescription = "Verified living victim POV"; return;
        }
        Vector3 candidate = best.Position;
        if (!exact && _replayFollowPosition is { } previous && deltaFrames > 0 && deltaFrames <= 15)
        {
            var smooth = Vector3.Lerp(previous, candidate, 1 - MathF.Exp(-(float)deltaFrames / 60 * 10));
            if (ReplayDirectorShots.SweepClear(previous, smooth, _directorClearance)
                && ReplayDirectorShots.IsClear(smooth, target, _directorClearance)) candidate = smooth;
        }
        var desiredFacing = best.Kind == ReplayDirectorShotKind.VictimPov ? best.Facing : (target - candidate).Normalized();
        var view = !exact && _directorFacing is { } previousFacing && _directorShot == best.Kind
            ? ReplayDirectorShots.LimitFacing(previousFacing, desiredFacing, deltaFrames) : desiredFacing;
        var right = Vector3.Cross(view, Vector3.UnitY);
        if (!float.IsFinite(right.LengthSquared) || right.LengthSquared < .000001f)
        { SetFreeCamera(false); ResetDirectorShot(); return; }
        SetFreeCamera(true); _cameraPosition = candidate; _cameraFacing = view; _cameraRight = right.Normalized();
        _cameraUp = Vector3.Cross(_cameraRight, _cameraFacing).Normalized();
        _cameraFov = MathHelper.DegreesToRadians(best.FovDegrees);
        _directorFacing = view; _directorShot = best.Kind; _replayFollowPosition = candidate;
        ReplayDirectorShotDescription = ReplayDirectorShots.Describe(best.Kind);
    }
}
