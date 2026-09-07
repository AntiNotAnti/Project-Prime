using System;
using MphRead.Entities;
using MphRead.Mods;
using MphRead.Hud;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace MphRead;

public enum SpectatorCameraMode { Free, FirstPerson, Chase, Orbit, AutoDirector }

/// <summary>Presentation-only camera state; never changes a replicated actor transform.</summary>
public sealed class SpectatorCameraController
{
    private int _held;
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
    public float FieldOfView { get; private set; } = 78;
    public float SpeedScale { get; private set; } = 1;
    public SpectatorCameraMode Mode { get; private set; }
    public int TargetSlot { get; private set; } = -1;

    internal void Poll(ScenePresentation presentation, KeyboardState keyboard)
    {
        if (!SpectatorMode.IsSpectating) { lock (TouchGate) { _touchCommand = 0; if (ReferenceEquals(_touchOwner, this)) _touchOwner = null; } _held = 0; TargetSlot = -1; _objective = null; Mode = SpectatorCameraMode.Free; return; }
        if (!ReferenceEquals(_room, presentation.World.Room))
        { lock (TouchGate) _touchCommand = 0; _room = presentation.World.Room; _objective = null; TargetSlot = -1; Mode = SpectatorCameraMode.Free; presentation.SetFreeCamera(true); }
        int keys = (keyboard.IsKeyDown(Keys.F1) ? 1 : 0) | (keyboard.IsKeyDown(Keys.F2) ? 2 : 0)
            | (keyboard.IsKeyDown(Keys.F3) ? 4 : 0) | (keyboard.IsKeyDown(Keys.F4) ? 8 : 0)
            | (keyboard.IsKeyDown(Keys.Minus) ? 16 : 0) | (keyboard.IsKeyDown(Keys.Equal) ? 32 : 0)
            | (keyboard.IsKeyDown(Keys.LeftBracket) ? 64 : 0) | (keyboard.IsKeyDown(Keys.RightBracket) ? 128 : 0);
        int pressed = keys & ~_held; _held = keys;
        lock (TouchGate)
        {
            _touchOwner = this;
            pressed |= _touchCommand; _touchCommand = 0;
        }
        if (Mods.ClientInputState.PauseOpen || Mods.Chat.ChatBox.Composing) return;
        ApplyCommands(presentation, pressed);
        if (Mode == SpectatorCameraMode.AutoDirector && ++_directorTick >= 300)
        { _directorTick = 0; Select(1); }
        _orbit = (_orbit + .006f) % MathF.Tau;
        if (SpectatorMode.FreeCamera) Mode = SpectatorCameraMode.Free;
        else if (Mode == SpectatorCameraMode.Free) Mode = SpectatorCameraMode.FirstPerson;
        if (Mode == SpectatorCameraMode.FirstPerson) TargetSlot = PlayerEntity.MainPlayerIndex;
    }

    internal void ApplyCommands(ScenePresentation presentation, int pressed)
    {
        if (!SpectatorMode.IsSpectating || Mods.ClientInputState.PauseOpen || Mods.Chat.ChatBox.Composing) return;
        if ((pressed & 16) != 0) FieldOfView = Math.Clamp(FieldOfView - 5, 40, 110);
        if ((pressed & 32) != 0) FieldOfView = Math.Clamp(FieldOfView + 5, 40, 110);
        if ((pressed & 64) != 0) SpeedScale = Math.Clamp(SpeedScale / 2, .25f, 4);
        if ((pressed & 128) != 0) SpeedScale = Math.Clamp(SpeedScale * 2, .25f, 4);
        if ((pressed & 8) != 0)
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
        if ((pressed & 1) != 0)
        {
            _objective = null;
            Mode = (SpectatorCameraMode)(((int)Mode + 1) % 5);
            if (Mode != SpectatorCameraMode.Free && TargetSlot < 0) Select(1);
            presentation.SetFreeCamera(Mode == SpectatorCameraMode.Free || TargetSlot < 0);
        }
        if ((pressed & 2) != 0) Select(1);
        if ((pressed & 4) != 0) Select(-1);
    }

    private void Select(int direction)
    {
        _objective = null;
        int start = TargetSlot >= 0 ? TargetSlot : PlayerEntity.MainPlayerIndex;
        for (int offset = 1; offset <= 8; offset++)
        {
            int slot = (start + direction * offset + 16) % 8;
            if (slot >= PlayerEntity.Players.Count || !PlayerEntity.Players[slot].LoadFlags.TestFlag(LoadFlags.Active)) continue;
            TargetSlot = slot;
            SpectatorMode.SelectTarget(slot);
            return;
        }
        TargetSlot = -1;
    }

    internal void Draw(ScenePresentation presentation)
    {
        if (!SpectatorMode.IsSpectating || PlayerEntity.Players.Count == 0) return;
        presentation.DrawHudFlatBox(5, 4, 251, 29, new Vector4(0, 0, 0, .7f));
        var hud = PlayerEntity.Main.GetPresentation();
        hud.DrawText2D(46, 7, Align.Center, 0, $"{Mode}", scale: .45f);
        hud.DrawText2D(128, 7, Align.Center, 0, "TARGET", scale: .45f);
        hud.DrawText2D(210, 7, Align.Center, 0, "OBJECTIVE", scale: .45f);
        hud.DrawText2D(36, 19, Align.Center, 0, $"FOV {FieldOfView:0} -", scale: .45f);
        hud.DrawText2D(98, 19, Align.Center, 0, "FOV +", scale: .45f);
        hud.DrawText2D(160, 19, Align.Center, 0, $"{SpeedScale:0.##}x -", scale: .45f);
        hud.DrawText2D(222, 19, Align.Center, 0, "SPEED +", scale: .45f);
        if (_objective != null)
        {
            string status = _objective is NodeDefenseEntity node
                ? $"NODE TEAM {node.CurrentTeam} {(node.Contested ? "CONTESTED" : "")}"
                : _objective is OctolithFlagEntity flag ? $"OCTOLITH {(flag.Carrier != null ? "CARRIED" : flag.AtBase ? "AT BASE" : "DROPPED")}" : "";
            hud.DrawText2D(128, 33, Align.Center, 0, status, scale: .55f);
        }
        else if (TargetSlot is >= 0 and < 8)
        {
            PlayerEntity target = PlayerEntity.Players[TargetSlot];
            var stats = presentation.World.Match.Players[TargetSlot];
            string ammo = "";
            if (Mods.Network.AuthoritativePlay.Current is { } play)
                foreach (var snapshot in play.Client.SnapshotPlayers)
                    if (snapshot.Slot == TargetSlot) { ammo = $" AMMO {snapshot.AmmoUa}/{snapshot.AmmoMissiles}"; break; }
            hud.DrawText2D(128, 33, Align.Center, 0,
                $"{GameState.Nicknames[TargetSlot]}  {target.Hunter}  HP {target.Health}  {target.CurrentWeapon}{ammo}", scale: .5f);
            hud.DrawText2D(128, 42, Align.Center, 0,
                $"K/D/A {stats.Kills}/{stats.Deaths}/{stats.Assists}  SCORE {stats.Points}", scale: .5f);
        }
    }

    internal bool TryView(out Matrix4 view)
    {
        view = Matrix4.Identity;
        if (SpectatorMode.IsSpectating && _objective != null)
        {
            Vector3 center = _objective.Position + Vector3.UnitY;
            view = Matrix4.LookAt(center + new Vector3(MathF.Sin(_orbit) * 7, 4, MathF.Cos(_orbit) * 7), center, Vector3.UnitY);
            return true;
        }
        if (!SpectatorMode.IsSpectating || Mode is SpectatorCameraMode.Free or SpectatorCameraMode.FirstPerson
            || TargetSlot < 0 || TargetSlot >= PlayerEntity.Players.Count) return false;
        PlayerEntity player = PlayerEntity.Players[TargetSlot];
        if (!player.LoadFlags.TestFlag(LoadFlags.Active)) return false;
        Vector3 target = player.Position + Vector3.UnitY;
        Vector3 offset = Mode == SpectatorCameraMode.Orbit
            ? new Vector3(MathF.Sin(_orbit) * 6, 3, MathF.Cos(_orbit) * 6)
            : -player.FacingVector * 5 + Vector3.UnitY * 2;
        view = Matrix4.LookAt(target + offset, target, Vector3.UnitY);
        return true;
    }
}
