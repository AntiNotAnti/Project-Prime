using System;
using System.Collections.Generic;
using MphRead.Mods.Replay;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace MphRead.Mods.Launcher.Core;

/// <summary>Replay preview camera input, without a toolkit control or a competing playback clock.</summary>
public sealed class TheatreViewportController
{
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private readonly HashSet<Keys> _held = new();
    private double? _dragX, _dragY;
    private float _yaw, _pitch;
    public bool Focused { get; private set; }

    public void Focus() { Verify(); Focused = true; }
    public void Release()
    {
        Verify(); Focused = false; _held.Clear(); _dragX = _dragY = null; _yaw = _pitch = 0;
    }
    public void PointerDownInWindow(double x, double y)
    {
        Verify(); Focus(); ReplayCamera.SetMode(ReplayCameraMode.Free); ReplayCamera.Director = false;
        _dragX = x; _dragY = y;
    }
    public void PointerMoveInWindow(double x, double y)
    {
        Verify();
        if (_dragX is not { } previousX || _dragY is not { } previousY) return;
        _yaw += (float)(x - previousX) * .003f; _pitch -= (float)(y - previousY) * .003f;
        _dragX = x; _dragY = y;
    }
    public void PointerUp() { Verify(); _dragX = _dragY = null; }
    public bool KeyDown(Keys key)
    {
        Verify(); if (!Focused) return false;
        if (key == Keys.Escape) { Release(); return true; }
        if (_held.Add(key)) ReplayInput.HandleKey(key, editor: true);
        return true;
    }
    public bool KeyUp(Keys key) { Verify(); _held.Remove(key); return Focused; }
    public void Poll(Scene scene)
    {
        Verify();
        if (!Focused || ReplayVideoExporter.Active) return;
        float Axis(Keys positive, Keys negative) => (_held.Contains(positive) ? 1 : 0) - (_held.Contains(negative) ? 1 : 0);
        float speed = _held.Contains(Keys.LeftShift) || _held.Contains(Keys.RightShift) ? .5f : .1f;
        scene.MoveReplayEditorCamera(new Vector3(Axis(Keys.D, Keys.A), Axis(Keys.E, Keys.V), Axis(Keys.W, Keys.S)) * speed, _yaw, _pitch);
        _yaw = _pitch = 0;
    }
    private void Verify() { if (_owner != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Replay viewport input belongs to the engine thread."); }
}
