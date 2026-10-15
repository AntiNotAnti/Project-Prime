using System;
using MphRead.Mods.Input;
using OpenTK.Mathematics;

namespace MphRead;

// A presentation-only layout wrapper. Every simulation entry point and stored
// packet continues to use the physical framebuffer/window dimensions.
// The 3D scene, HUD and GL composite are scoped to a single LCD during drawing.
public partial class Scene
{
    private Vector2i _dsPhysicalOutput;
    private bool _dsBottomPass;

    internal bool DsTopPass => _dsPhysicalOutput.X > 0 && !_dsBottomPass;
    internal bool UseDsLayout =>
        DsLayout.Active && _dsPhysicalOutput.X == 0
        && !Services.IsReplica && !Mods.Network.DemoPlayback.IsActive
        && !Mods.KillCam.IsActive && !ExportingReplay
        && !Mods.ThumbnailMode.Active
        && Players.Items.Count > 0 && CameraMode == CameraMode.Player
        && !GameState.MenuPause && !Mods.PauseMenu.Open;

    private Vector4i DsViewport(bool bottom)
    {
        // GL viewport origin is bottom-left, whereas DS layout coordinates
        // and tablet contacts are measured downward from the top-left.
        Vector4i area = bottom
            ? DsLayout.Bounds(_dsPhysicalOutput.X, _dsPhysicalOutput.Y).Bottom
            : DsLayout.Bounds(_dsPhysicalOutput.X, _dsPhysicalOutput.Y).Top;
        return new Vector4i(area.X,
            _dsPhysicalOutput.Y - area.Y - area.W, area.Z, area.W);
    }

    public void OnDrawFrame()
    {
        Vector2i physical = Size;
        bool stacked = UseDsLayout && physical.X >= 5 && physical.Y >= 6;
        if (stacked)
        {
            _dsPhysicalOutput = physical;
            Vector4i top = DsLayout.Bounds(physical.X, physical.Y).Top;
            Size = new Vector2i(top.Z, top.W);
        }
        try { OnDrawFrameCore(); }
        finally
        {
            Size = physical;
            _dsPhysicalOutput = default;
        }
    }

    public bool OnRenderFrame()
    {
        Vector2i physical = Size;
        bool stacked = UseDsLayout && physical.X >= 5 && physical.Y >= 6;
        if (stacked)
        {
            _dsPhysicalOutput = physical;
            Vector4i top = DsLayout.Bounds(physical.X, physical.Y).Top;
            Size = new Vector2i(top.Z, top.W);
        }
        try
        {
            bool rendered = OnRenderFrameCore();
            if (stacked && rendered)
            {
                // A second HUD pass uses the same framebuffer and sprite shader,
                // but a different viewport. No second scene or simulation step.
                _dsBottomPass = true;
                try
                {
                    BeginHudPass();
                    try { Players.Main.ModDrawDsBottomScreen(); }
                    finally { EndHudPass(); }
                }
                finally { _dsBottomPass = false; }
            }
            return rendered;
        }
        finally
        {
            _dsBottomPass = false;
            Size = physical;
            _dsPhysicalOutput = default;
        }
    }
}
