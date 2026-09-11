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
    public BroadcastDirector Director { get; } = new();
    public BroadcastHud BroadcastHud { get; } = new();
    public float FieldOfView { get; private set; } = 78;
    public float SpeedScale { get; private set; } = 1;
    public SpectatorCameraMode Mode { get; private set; }
    public int TargetSlot { get; private set; } = -1;

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
    }

    internal void FocusHighlight(ScenePresentation presentation,
        in ReplayHighlight highlight)
    {
        if (!highlight.Focus.IsValid) return;
        TargetSlot = highlight.Focus.Slot;
        _objective = null;
        SpectatorMode.SelectTarget(presentation.World, TargetSlot);
        _directorCameraMode = highlight.Kind is HighlightKind.Headshot
            or HighlightKind.DoubleKill or HighlightKind.TripleKill
            ? SpectatorCameraMode.FirstPerson : SpectatorCameraMode.Chase;
        Mode = SpectatorCameraMode.AutoDirector;
        presentation.SetFreeCamera(false);
        Director.Lock(BroadcastFocus.Player(TargetSlot), ObservationContext.Capture(
            presentation.World, presentation, ObservationSourceKind.Replay));
    }

    internal void Poll(ScenePresentation presentation, KeyboardState keyboard)
    {
        if (_external) return;
        if (!SpectatorMode.IsSpectating) { lock (TouchGate) { _touchCommand = 0; if (ReferenceEquals(_touchOwner, this)) _touchOwner = null; } _held = SpectatorCommand.None; TargetSlot = -1; _objective = null; Mode = SpectatorCameraMode.Free; return; }
        if (!ReferenceEquals(_room, presentation.World.Room))
        { lock (TouchGate) _touchCommand = 0; _room = presentation.World.Room; _objective = null; TargetSlot = -1; Mode = SpectatorCameraMode.Free; _hasCameraPose = false; _directorTick = 0; Director.Reset(); presentation.SetFreeCamera(true); }
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
            ApplyDirector(presentation, Director.Update(ObservationContext.Capture(
                presentation.World, presentation)));
        }
        if (Mode == SpectatorCameraMode.Orbit) _orbit = (_orbit + .006f) % MathF.Tau;
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
                ApplyDirector(presentation, Director.Resume(ObservationContext.Capture(
                    presentation.World, presentation)));
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

    private void ApplyDirector(ScenePresentation presentation, BroadcastFocus focus)
    {
        if (focus.Kind == BroadcastFocusKind.Player)
        {
            _objective = null;
            TargetSlot = focus.Id;
            SpectatorMode.SelectTarget(presentation.World, focus.Id);
            bool precision = false;
            ObservationContext context = ObservationContext.Capture(
                presentation.World, presentation);
            foreach (KillFeedEntry entry in context.CombatFeedback)
                if (context.TryGetPlayer(focus.Id, out ObservationPlayer focused)
                    && focused.Identity.IsValid && entry.Killer == focused.Identity
                    && unchecked(context.DeliveredTick - entry.Tick) < 120)
                { precision = true; break; }
            _directorCameraMode = precision
                ? SpectatorCameraMode.FirstPerson : SpectatorCameraMode.Chase;
            Mode = SpectatorCameraMode.AutoDirector;
            presentation.SetFreeCamera(false);
        }
        else if (focus.Kind == BroadcastFocusKind.Objective)
        {
            foreach (EntityBase entity in presentation.World.Entities)
                if (entity.Id == focus.Id) { _objective = entity; break; }
            _directorCameraMode = SpectatorCameraMode.Orbit;
            Mode = SpectatorCameraMode.AutoDirector;
            presentation.SetFreeCamera(false);
        }
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
        BroadcastHud.Draw(presentation, BroadcastHud.Compose(context, focus,
            Mode, FieldOfView, SpeedScale));
    }

    internal bool TryView(Scene scene, out Matrix4 view)
    {
        view = Matrix4.Identity;
        bool active = _external || SpectatorMode.IsSpectating;
        if (active && _objective != null)
        {
            Vector3 center = _objective.Position + Vector3.UnitY;
            Vector3 desired = center + new Vector3(MathF.Sin(_orbit) * 7, 4,
                MathF.Cos(_orbit) * 7);
            ResolveCamera(scene, center, desired, out Vector3 cameraPosition,
                out Vector3 cameraTarget);
            view = Matrix4.LookAt(cameraPosition, cameraTarget, Vector3.UnitY);
            return true;
        }
        SpectatorCameraMode viewMode = Mode == SpectatorCameraMode.AutoDirector
            ? _directorCameraMode : Mode;
        if (!active || viewMode is SpectatorCameraMode.Free or SpectatorCameraMode.FirstPerson
            || TargetSlot < 0 || TargetSlot >= scene.Players.Count) return false;
        PlayerEntity player = scene.Players[TargetSlot];
        if (!player.LoadFlags.TestFlag(LoadFlags.Active)) return false;
        Vector3 target = player.Position + Vector3.UnitY;
        Vector3 offset = viewMode == SpectatorCameraMode.Orbit
            ? new Vector3(MathF.Sin(_orbit) * 6, 3, MathF.Cos(_orbit) * 6)
            : -player.FacingVector * 5 + Vector3.UnitY * 2;
        ResolveCamera(scene, target, target + offset, out Vector3 position,
            out Vector3 smoothedTarget);
        view = Matrix4.LookAt(position, smoothedTarget, Vector3.UnitY);
        return true;
    }

    private void ResolveCamera(Scene scene, Vector3 target, Vector3 desired,
        out Vector3 position, out Vector3 smoothedTarget)
    {
        CollisionResult collision = default;
        desired = ClampCameraPosition(scene, target, desired, ref collision);
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
        _smoothedPosition = safePosition;
        position = safePosition;
        smoothedTarget = _smoothedTarget;
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
