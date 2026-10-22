using MphRead.Mods.Input;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace MphRead;

public partial class Scene
{
    // Scope only the existing render path to a 4:3 LCD. Simulation, pointer
    // coordinates and launcher overlays retain the real window dimensions.
    private Vector2i _dsWindowSize;
    internal bool DsTopPass => _dsWindowSize.X > 0;
    internal bool UseDsLayout => DsLayout.Active && !Services.IsReplica
        && !Mods.Network.DemoPlayback.IsActive && !ExportingReplay
        && CameraMode == CameraMode.Player;

    private Vector2i OutputViewportOrigin
    {
        get
        {
            if (!DsTopPass) return Vector2i.Zero;
            var top = DsLayout.Bounds(_dsWindowSize.X, _dsWindowSize.Y).Top;
            // Authored/input coordinates begin at the top; OpenGL begins at the bottom.
            return new(top.X, _dsWindowSize.Y - top.Y - top.W);
        }
    }
    private void SetOutputViewport()
    { var origin = OutputViewportOrigin; GL.Viewport(origin.X, origin.Y, Size.X, Size.Y); }

    public void OnDrawFrame()
    {
        var window = Size;
        bool ds = UseDsLayout;
        if (ds)
        {
            _dsWindowSize = window;
            var top = DsLayout.Bounds(window.X, window.Y).Top;
            Size = new Vector2i(top.Z, top.W);
        }
        try { OnDrawFrameCore(); }
        finally { Size = window; _dsWindowSize = default; }
    }

    public bool OnRenderFrame()
    {
        var window = Size;
        bool ds = UseDsLayout;
        bool result;
        if (ds)
        {
            _dsWindowSize = window;
            var top = DsLayout.Bounds(window.X, window.Y).Top;
            Size = new Vector2i(top.Z, top.W);
        }
        try { result = OnRenderFrameCore(); }
        finally { Size = window; _dsWindowSize = default; }
        if (ds && result)
        {
            BeginHudPass();
            try { Players.Main.ModDrawDsBottomScreen(); }
            finally { EndHudPass(); }
        }
        return result;
    }
}
