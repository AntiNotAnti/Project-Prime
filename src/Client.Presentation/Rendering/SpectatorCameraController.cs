using System;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Combat;
using MphRead.Mods;
using MphRead.Mods.Network;
using MphRead.Hud;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace MphRead;

public enum SpectatorCameraMode { Free, FirstPerson, Chase, Orbit, AutoDirector }

[Flags]
public enum SpectatorCommand
{
    None = 0,
    CameraMode = 1,
    NextTarget = 2,
    PreviousTarget = 4,
    Objective = 8,
    FovDown = 16,
    FovUp = 32,
    SpeedDown = 64,
    SpeedUp = 128,
    Director = 256,
    LockTarget = 512,
    HudMode = 1024,
    FreeCamera = 2048
}

/// <summary>Presentation-only camera state; never changes a replicated actor transform.</summary>
public sealed class SpectatorCameraController
{
    private SpectatorCommand _held;
    private static readonly object TouchGate = new();
    private static SpectatorCameraController? _touchOwner;
    private int _touchCommand;
    internal static int PointerCommand(float x, float y)
    {
        if (!float.IsFinite(x) || !float.IsFinite(y) || x < 5 || x > 251 || y < 4 || y > 29) return 0;
        return y < 16 ? (x < 87 ? 1 : x < 169 ? 2 : 8)
            : (x < 67 ? 16 : x < 129 ? 32 : x < 191 ? 64 : 128);
    }
    public static bool QueuePointerDown(float x, float y)
    {
        if (!SpectatorMode.IsSpectating || Mods.ClientInputState.PauseOpen || Mods.Chat.ChatBox.Composing
            || PointerCommand(x, y) == 0) return false;
        lock (TouchGate)
        {
            if (_touchOwner == null) return false;
            _touchOwner._touchCommand = PointerCommand(x, y);
            return true;
        }
    }
    public void CycleMode(ScenePresentation presentation) => ApplyCommands(presentation, 1);
    public void CycleTarget(ScenePresentation presentation) => ApplyCommands(presentation, 2);
    private uint _directorTick;
    private float _orbit;
    private EntityBase? _objective;
    private RoomEntity? _room;
    private bool _external;
    private bool _hasCameraPose;
    private Vector3 _smoothedPosition;
    private Vector3 _smoothedTarget;
    private SpectatorCameraMode _directorCameraMode = SpectatorCameraMode.Chase;
    private BroadcastFocus _directorFocus;
    private CombatActor _directorFocusActor = CombatActor.None;
    private BroadcastShotType _directorShot = BroadcastShotType.TightChase;
    private bool _transitionPending;
    private bool _hasTransition;
    private BroadcastCameraTransition _transition;
    private bool _hasRenderedPose;
    private BroadcastCameraPose _renderedPose;
    public BroadcastDirector Director { get; } = new();
    public BroadcastShotPlanner ShotPlanner { get; } = new();
    public BroadcastHud BroadcastHud { get; } = new();
    public float FieldOfView { get; private set; } = 78;
    public float SpeedScale { get; private set; } = 1;
    public SpectatorCameraMode Mode { get; private set; }
    public int TargetSlot { get; private set; } = -1;
    internal bool DetachedViewActive { get; private set; }
    public BroadcastCameraTransition? CameraTransition
        => _hasTransition ? _transition : null;

    internal void ConfigureKillcam(int targetSlot, SpectatorCameraMode mode)
    {
        if (targetSlot is < 0 or >= PlayerEntity.SlotCapacity)
            throw new ArgumentOutOfRangeException(nameof(targetSlot));
        if (mode is not (SpectatorCameraMode.FirstPerson or SpectatorCameraMode.Chase))
            throw new ArgumentOutOfRangeException(nameof(mode));
        _external = true;
        TargetSlot = targetSlot;
        Mode = mode;
        _objective = null;
        _hasCameraPose = false;
        ResetBroadcastCamera();
    }

    internal bool FocusHighlight(ScenePresentation presentation,
        in ReplayHighlight highlight)
    {
        if (!highlight.Focus.IsValid) return false;
        return FocusReplayActor(presentation, highlight.Focus,
            highlight.Kind is HighlightKind.Headshot
                or HighlightKind.DoubleKill or HighlightKind.TripleKill
                ? SpectatorCameraMode.FirstPerson : SpectatorCameraMode.Chase);
    }

    internal bool FocusReplayActor(ScenePresentation presentation,
        CombatActor actor, SpectatorCameraMode mode = SpectatorCameraMode.Chase)
    {
        if (!actor.IsValid || actor.Slot >= presentation.World.Players.Count
            || presentation.World.Players[actor.Slot].CombatIdentity != actor)
            return false;
        TargetSlot = actor.Slot;
        _objective = null;
        SpectatorMode.SelectTarget(presentation.World, TargetSlot);
        _directorCameraMode = mode;
        _directorFocus = BroadcastFocus.Player(TargetSlot);
        _directorFocusActor = actor;
        _directorShot = _directorCameraMode == SpectatorCameraMode.FirstPerson
            ? BroadcastShotType.FirstPerson : BroadcastShotType.TightChase;
        _transitionPending = true;
        Mode = SpectatorCameraMode.AutoDirector;
        presentation.SetFreeCamera(false);
        Director.Lock(BroadcastFocus.Player(TargetSlot), ObservationContext.Capture(
            presentation.World, presentation, ObservationSourceKind.Replay));
        return true;
    }

    internal void Poll(ScenePresentation presentation, KeyboardState keyboard)
    {
        if (_external) return;
        if (!SpectatorMode.IsSpectating) { lock (TouchGate) { _touchCommand = 0; if (ReferenceEquals(_touchOwner, this)) _touchOwner = null; } _held = SpectatorCommand.None; TargetSlot = -1; _objective = null; Mode = SpectatorCameraMode.Free; ResetBroadcastCamera(); return; }
        if (!ReferenceEquals(_room, presentation.World.Room))
        { lock (TouchGate) _touchCommand = 0; _room = presentation.World.Room; _objective = null; TargetSlot = -1; Mode = SpectatorCameraMode.Free; _hasCameraPose = false; _directorTick = 0; Director.Reset(); ResetBroadcastCamera(); presentation.SetFreeCamera(true); }
        SpectatorCommand keys = SpectatorCommandInput.FromKeyboard(keyboard);
        SpectatorCommand pressed = keys & ~_held; _held = keys;
        lock (TouchGate)
        {
            _touchOwner = this;
            pressed |= (SpectatorCommand)_touchCommand; _touchCommand = 0;
        }
        if (Mods.ClientInputState.PauseOpen || Mods.Chat.ChatBox.Composing) return;
        ApplyCommand(presentation, pressed);
        if (Mode == SpectatorCameraMode.AutoDirector && ++_directorTick >= BroadcastDirector.CadenceTicks)
        {
            _directorTick = 0;
            ObservationContext context = ObservationContext.Capture(
                presentation.World, presentation);
            ApplyDirector(presentation, Director.Update(context), context);
        }
        if (Mode == SpectatorCameraMode.Orbit
            || Mode == SpectatorCameraMode.AutoDirector
            && _directorShot is BroadcastShotType.Orbit or BroadcastShotType.ObjectiveWide)
            _orbit = (_orbit + .006f) % MathF.Tau;
        if (SpectatorMode.FreeCamera) Mode = SpectatorCameraMode.Free;
        else if (Mode == SpectatorCameraMode.Free) Mode = SpectatorCameraMode.FirstPerson;
        if (Mode == SpectatorCameraMode.FirstPerson) TargetSlot = presentation.World.LocalPlayerSlot;
    }

    internal void ApplyCommands(ScenePresentation presentation, int pressed)
    {
        ApplyCommand(presentation, (SpectatorCommand)pressed);
    }

    public void ApplyCommand(ScenePresentation presentation, SpectatorCommand command)
    {
        if (!SpectatorMode.IsSpectating || Mods.ClientInputState.PauseOpen || Mods.Chat.ChatBox.Composing) return;
        if ((command & SpectatorCommand.FovDown) != 0) FieldOfView = Math.Clamp(FieldOfView - 5, 40, 110);
        if ((command & SpectatorCommand.FovUp) != 0) FieldOfView = Math.Clamp(FieldOfView + 5, 40, 110);
        if ((command & SpectatorCommand.SpeedDown) != 0) SpeedScale = Math.Clamp(SpeedScale / 2, .25f, 4);
        if ((command & SpectatorCommand.SpeedUp) != 0) SpeedScale = Math.Clamp(SpeedScale * 2, .25f, 4);
        if ((command & SpectatorCommand.Objective) != 0)
        {
            EntityBase? first = null, next = null; bool found = _objective == null;
            foreach (EntityBase entity in presentation.World.Entities)
            {
                if (entity is not (OctolithFlagEntity or NodeDefenseEntity)) continue;
                first ??= entity;
                if (found) { next = entity; break; }
                if (ReferenceEquals(entity, _objective)) found = true;
            }
            _objective = next ?? first;
            if (_objective != null) { Mode = SpectatorCameraMode.Orbit; presentation.SetFreeCamera(false); }
        }
        if ((command & SpectatorCommand.CameraMode) != 0)
        {
            _objective = null;
            Mode = (SpectatorCameraMode)(((int)Mode + 1) % 5);
            if (Mode != SpectatorCameraMode.Free && TargetSlot < 0) Select(presentation.World, 1);
            presentation.SetFreeCamera(Mode == SpectatorCameraMode.Free || TargetSlot < 0);
        }
        if ((command & SpectatorCommand.NextTarget) != 0) Select(presentation.World, 1);
        if ((command & SpectatorCommand.PreviousTarget) != 0) Select(presentation.World, -1);
        if ((command & SpectatorCommand.Director) != 0)
        {
            Mode = Mode == SpectatorCameraMode.AutoDirector
                ? SpectatorCameraMode.Chase : SpectatorCameraMode.AutoDirector;
            if (Mode == SpectatorCameraMode.AutoDirector)
            {
                ObservationContext context = ObservationContext.Capture(
                    presentation.World, presentation);
                ApplyDirector(presentation, Director.Resume(context), context);
            }
        }
        if ((command & SpectatorCommand.LockTarget) != 0)
        {
            ObservationContext context = ObservationContext.Capture(
                presentation.World, presentation);
            if (Director.IsManualLock) Director.Resume(context);
            else if (TargetSlot >= 0) Director.Lock(BroadcastFocus.Player(TargetSlot), context);
        }
        if ((command & SpectatorCommand.HudMode) != 0) BroadcastHud.CycleMode();
        if ((command & SpectatorCommand.FreeCamera) != 0)
        {
            Mode = SpectatorCameraMode.Free;
            presentation.SetFreeCamera(true);
        }
    }

    private void ApplyDirector(ScenePresentation presentation, BroadcastFocus focus,
        ObservationContext context)
    {
        if (focus.Kind == BroadcastFocusKind.Player)
        {
            if (!context.TryGetPlayer(focus.Id, out ObservationPlayer focused)
                || !focused.Identity.IsValid || focus.Id < 0
                || focus.Id >= presentation.World.Players.Count
                || presentation.World.Players[focus.Id].CombatIdentity != focused.Identity)
                return;
            _objective = null;
            TargetSlot = focus.Id;
            SpectatorMode.SelectTarget(presentation.World, focus.Id);
            ApplyShotSelection(ShotPlanner.Select(context, focus));
            Mode = SpectatorCameraMode.AutoDirector;
            presentation.SetFreeCamera(false);
        }
        else if (focus.Kind == BroadcastFocusKind.Objective)
        {
            EntityBase? selected = null;
            foreach (EntityBase entity in presentation.World.Entities)
                if (entity.Id == focus.Id) { selected = entity; break; }
            if (selected == null || !context.TryGetObjective(focus.Id, out _)) return;
            _objective = selected;
            ApplyShotSelection(ShotPlanner.Select(context, focus));
            Mode = SpectatorCameraMode.AutoDirector;
            presentation.SetFreeCamera(false);
        }
        else
        {
            _objective = null;
            TargetSlot = -1;
            ApplyShotSelection(ShotPlanner.Select(context, BroadcastFocus.None));
            Mode = SpectatorCameraMode.Free;
            presentation.SetFreeCamera(true);
        }
    }

    private void ApplyShotSelection(in BroadcastShotSelection selection)
    {
        bool changed = selection.Focus != _directorFocus
            || selection.FocusActor != _directorFocusActor
            || selection.Shot != _directorShot;
        _directorFocus = selection.Focus;
        _directorFocusActor = selection.FocusActor;
        _directorShot = selection.Shot;
        _directorCameraMode = selection.Shot switch
        {
            BroadcastShotType.FirstPerson => SpectatorCameraMode.FirstPerson,
            BroadcastShotType.Orbit or BroadcastShotType.ObjectiveWide
                => SpectatorCameraMode.Orbit,
            _ => SpectatorCameraMode.Chase
        };
        _transitionPending |= changed;
    }

    private void Select(Scene scene, int direction)
    {
        _objective = null;
        int start = TargetSlot >= 0 ? TargetSlot : scene.LocalPlayerSlot;
        for (int offset = 1; offset <= 8; offset++)
        {
            int slot = (start + direction * offset + 16) % 8;
            if (slot >= scene.Players.Count || !scene.Players[slot].LoadFlags.TestFlag(LoadFlags.Active)) continue;
            TargetSlot = slot;
            SpectatorMode.SelectTarget(scene, slot);
            return;
        }
        TargetSlot = -1;
    }

    internal void Draw(ScenePresentation presentation)
    {
        if (_external || !SpectatorMode.IsSpectating
            || presentation.World.Players.Count == 0) return;
        ObservationContext context = ObservationContext.Capture(
            presentation.World, presentation);
        BroadcastFocus focus = _objective != null
            ? BroadcastFocus.Objective(_objective.Id)
            : TargetSlot >= 0 ? BroadcastFocus.Player(TargetSlot) : BroadcastFocus.None;
        BroadcastHud.Draw(presentation, BroadcastHud.Compose(context, focus));
    }

    internal bool TryView(Scene scene, out Matrix4 view)
    {
        view = Matrix4.Identity;
        DetachedViewActive = false;
        bool active = _external || SpectatorMode.IsSpectating;
        if (active && _objective != null)
        {
            Vector3 center = _objective.Position + Vector3.UnitY;
            float radius = Mode == SpectatorCameraMode.AutoDirector
                && _directorShot == BroadcastShotType.ObjectiveWide ? 10 : 7;
            float height = Mode == SpectatorCameraMode.AutoDirector
                && _directorShot == BroadcastShotType.ObjectiveWide ? 5 : 4;
            Vector3 desired = center + new Vector3(MathF.Sin(_orbit) * radius, height,
                MathF.Cos(_orbit) * radius);
            ResolveCamera(scene, center, desired, out Vector3 cameraPosition,
                out Vector3 cameraTarget, out bool collisionAdjusted);
            BroadcastCameraPose pose = ApplyCameraTransition(scene,
                new(cameraPosition, cameraTarget), collisionAdjusted);
            view = Matrix4.LookAt(pose.Position, pose.Target, Vector3.UnitY);
            DetachedViewActive = true;
            return true;
        }
        SpectatorCameraMode viewMode = Mode == SpectatorCameraMode.AutoDirector
            ? _directorCameraMode : Mode;
        if (active && Mode == SpectatorCameraMode.AutoDirector
            && _directorFocus.Kind == BroadcastFocusKind.Player
            && (TargetSlot < 0 || TargetSlot >= scene.Players.Count
                || !IsExactDirectorActor(_directorFocusActor,
                    scene.Players[TargetSlot].CombatIdentity)))
        {
            // Returning false would fall through to the renderer's selected
            // player camera, which is exactly the reused-slot view we must not
            // expose. Hold the prior presentation pose (or a neutral world
            // pose) until the director selects a new exact actor.
            BroadcastCameraPose safe = _hasRenderedPose ? _renderedPose
                : new(new Vector3(0, 5, 8), Vector3.UnitY);
            view = Matrix4.LookAt(safe.Position, safe.Target, Vector3.UnitY);
            _directorFocus = BroadcastFocus.None;
            _directorFocusActor = CombatActor.None;
            TargetSlot = -1;
            ResetRenderedPose();
            DetachedViewActive = true;
            return true;
        }
        if (!active || viewMode is SpectatorCameraMode.Free or SpectatorCameraMode.FirstPerson
            || TargetSlot < 0 || TargetSlot >= scene.Players.Count)
        {
            ResetRenderedPose();
            return false;
        }
        PlayerEntity player = scene.Players[TargetSlot];
        if (!player.LoadFlags.TestFlag(LoadFlags.Active))
        {
            ResetRenderedPose();
            return false;
        }
        Vector3 target = player.Position + Vector3.UnitY;
        Vector3 offset;
        if (viewMode == SpectatorCameraMode.Orbit)
            offset = new Vector3(MathF.Sin(_orbit) * 6, 3, MathF.Cos(_orbit) * 6);
        else if (Mode == SpectatorCameraMode.AutoDirector
            && _directorShot == BroadcastShotType.WideChase)
            offset = -player.FacingVector * 8 + Vector3.UnitY * 4;
        else
            offset = -player.FacingVector * 5 + Vector3.UnitY * 2;
        ResolveCamera(scene, target, target + offset, out Vector3 position,
            out Vector3 smoothedTarget, out bool adjusted);
        BroadcastCameraPose playerPose = ApplyCameraTransition(scene,
            new(position, smoothedTarget), adjusted);
        view = Matrix4.LookAt(playerPose.Position, playerPose.Target, Vector3.UnitY);
        DetachedViewActive = true;
        return true;
    }

    internal static bool IsExactDirectorActor(CombatActor expected,
        CombatActor current)
        => expected.IsValid && current == expected;

    private void ResolveCamera(Scene scene, Vector3 target, Vector3 desired,
        out Vector3 position, out Vector3 smoothedTarget,
        out bool collisionAdjusted)
    {
        CollisionResult collision = default;
        Vector3 requested = desired;
        desired = ClampCameraPosition(scene, target, desired, ref collision);
        collisionAdjusted = desired != requested;
        if (!_hasCameraPose)
        {
            _smoothedPosition = desired;
            _smoothedTarget = target;
            _hasCameraPose = true;
        }
        else
        {
            // Stable critically-damped approximation at presentation rate.
            const float response = .18f;
            _smoothedPosition += (desired - _smoothedPosition) * response;
            _smoothedTarget += (target - _smoothedTarget) * response;
        }
        // The smoothed candidate can cross a wall between fixed updates even
        // when the newly requested endpoint was clear. Re-sweep the final
        // render position and clamp it before building the view matrix.
        Vector3 safePosition = ClampCameraPosition(scene, target,
            _smoothedPosition, ref collision);
        collisionAdjusted |= safePosition != _smoothedPosition;
        _smoothedPosition = safePosition;
        position = safePosition;
        smoothedTarget = _smoothedTarget;
    }

    private BroadcastCameraPose ApplyCameraTransition(Scene scene,
        BroadcastCameraPose current, bool collisionAdjusted)
    {
        uint tick = scene.Services.WorldServerTick;
        if (tick == 0) tick = unchecked((uint)scene.FrameCount);
        if (_transitionPending)
        {
            _transitionPending = false;
            if (_hasRenderedPose)
            {
                bool establishing = _directorShot is BroadcastShotType.ObjectiveWide
                    or BroadcastShotType.FreeEstablishing;
                _transition = new(_renderedPose, current, tick,
                    establishing ? 24u : 12u,
                    establishing ? BroadcastCameraTransitionKind.Establishing
                        : BroadcastCameraTransitionKind.Blend);
                _hasTransition = true;
            }
        }
        BroadcastCameraPose result = current;
        uint transitionAge = 0;
        if (_hasTransition)
        {
            transitionAge = _transition.Age(tick);
            result = _transition.Interpolate(current, tick);
            if (_transition.Complete(tick)) _hasTransition = false;
        }
        _renderedPose = result;
        _hasRenderedPose = true;
        ShotPlanner.UpdatePresentationDiagnostics(collisionAdjusted, transitionAge);
        return result;
    }

    private void ResetRenderedPose()
    {
        _hasRenderedPose = false;
        _hasTransition = false;
        _transitionPending = false;
        ShotPlanner.UpdatePresentationDiagnostics(false, 0);
    }

    private void ResetBroadcastCamera()
    {
        _directorFocus = BroadcastFocus.None;
        _directorFocusActor = CombatActor.None;
        _directorShot = BroadcastShotType.TightChase;
        _directorCameraMode = SpectatorCameraMode.Chase;
        _transitionPending = false;
        _hasTransition = false;
        _hasRenderedPose = false;
        ShotPlanner.Reset();
    }

    private static Vector3 ClampCameraPosition(Scene scene, Vector3 target,
        Vector3 desired, ref CollisionResult collision)
    {
        if (!CollisionDetection.CheckBetweenPoints(target, desired,
                TestFlags.Players, scene, ref collision)) return desired;
        Vector3 offset = desired - target;
        return target + offset * Math.Clamp(collision.Distance, 0, 1)
            + collision.Plane.Xyz * .15f;
    }
}

internal static class SpectatorCommandInput
{
    internal static SpectatorCommand FromKeyboard(KeyboardState keyboard)
    {
        SpectatorCommand command = SpectatorCommand.None;
        if (keyboard.IsKeyDown(Keys.F1)) command |= SpectatorCommand.CameraMode;
        if (keyboard.IsKeyDown(Keys.F2)) command |= SpectatorCommand.NextTarget;
        if (keyboard.IsKeyDown(Keys.F3)) command |= SpectatorCommand.PreviousTarget;
        if (keyboard.IsKeyDown(Keys.F4)) command |= SpectatorCommand.Objective;
        if (keyboard.IsKeyDown(Keys.Minus)) command |= SpectatorCommand.FovDown;
        if (keyboard.IsKeyDown(Keys.Equal)) command |= SpectatorCommand.FovUp;
        if (keyboard.IsKeyDown(Keys.LeftBracket)) command |= SpectatorCommand.SpeedDown;
        if (keyboard.IsKeyDown(Keys.RightBracket)) command |= SpectatorCommand.SpeedUp;
        if (keyboard.IsKeyDown(Keys.F5)) command |= SpectatorCommand.Director;
        // F6-F10 belong to replay transport. Keep broadcast actions disjoint so
        // replay/highlight observation never performs two commands per key press.
        if (keyboard.IsKeyDown(Keys.F11)) command |= SpectatorCommand.LockTarget;
        if (keyboard.IsKeyDown(Keys.F12)) command |= SpectatorCommand.HudMode;
        if (keyboard.IsKeyDown(Keys.Home)) command |= SpectatorCommand.FreeCamera;
        return command;
    }
}
