using MphRead;
using MphRead.Mods.Input;
using OpenTK.Mathematics;

internal static class SceneViewportChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var window = new Vector2i(1280,721);
        var viewport = new Vector2i(902,400);
        DsLayout.Enabled = true; PointerInput.StylusMode = true;
        // Replay core preparation adopts its actual inspector viewport while
        // the outer scene wrapper has already captured the full window size.
        MphRead.Mods.Network.DemoPlayback.IsActive = true;
        var scene = new Scene { Size = window };
        scene.Draw = () => { check(!scene.DsTopPass,"replay draw has no DS scope"); scene.Size = viewport; };
        scene.OnDrawFrame();
        check(scene.Size == viewport,"normal draw retains core replay viewport resize");
        scene.Size = window;
        scene.Render = () => { scene.Size = viewport; return true; };
        check(scene.OnRenderFrame() && scene.Size == viewport,"normal render retains core replay viewport resize");
        scene.Size = window;
        scene.Draw = () => { scene.Size = viewport; throw new InvalidOperationException("core draw fault"); };
        try { scene.OnDrawFrame(); } catch(InvalidOperationException) { }
        check(scene.Size == viewport && !scene.DsTopPass,"normal exception does not restore an unowned stale size");

        MphRead.Mods.Network.DemoPlayback.IsActive = false;
        scene.Size = window;
        var top = DsLayout.Bounds(window.X,window.Y).Top;
        var topSize = new Vector2i(top.Z,top.W);
        scene.Draw = () => { check(scene.DsTopPass && scene.Size == topSize,"DS draw receives actual LCD size"); };
        scene.OnDrawFrame();
        check(scene.Size == window && !scene.DsTopPass,"DS draw restores full window size");
        scene.Draw = () => { throw new InvalidOperationException("DS draw fault"); };
        try { scene.OnDrawFrame(); } catch(InvalidOperationException) { }
        check(scene.Size == window && !scene.DsTopPass,"DS draw exception restores scope");
        scene.Render = () => { check(scene.DsTopPass && scene.Size == topSize,"DS render receives actual LCD size"); return true; };
        check(scene.OnRenderFrame() && scene.Size == window && !scene.DsTopPass,"DS render restores scope");
        check(scene.Players.Main.BottomDraws == 1 && scene.HudBegins == 1 && scene.HudEnds == 1,"successful DS render draws bottom LCD once");
        scene.Render = () => false;
        check(!scene.OnRenderFrame() && scene.Size == window && scene.Players.Main.BottomDraws == 1,"unsuccessful DS render restores scope without bottom LCD");
        scene.Render = () => { throw new InvalidOperationException("DS render fault"); };
        try { scene.OnRenderFrame(); } catch(InvalidOperationException) { }
        check(scene.Size == window && !scene.DsTopPass && scene.Players.Main.BottomDraws == 1,"DS render exception restores scope without bottom LCD");
        DsLayout.Enabled = false;
    }
}

// Only core draw/render and native GL boundaries are supplied by this fixture.
// The production wrappers, ownership predicate and restoration run unchanged.
namespace MphRead
{
    public enum CameraMode { Player }
    public partial class Scene
    {
        public Vector2i Size { get; set; }
        public StubServices Services { get; } = new();
        public CameraMode CameraMode { get; set; }
        private bool ExportingReplay => false;
        public StubPlayers Players { get; } = new();
        public Action Draw = () => { };
        public Func<bool> Render = () => true;
        public int HudBegins, HudEnds;
        private void OnDrawFrameCore() => Draw();
        private bool OnRenderFrameCore() => Render();
        private void BeginHudPass() => HudBegins++;
        private void EndHudPass() => HudEnds++;
    }
    public sealed class StubServices { public bool IsReplica { get; set; } }
    public sealed class StubPlayers { public StubPlayer Main { get; } = new(); }
    public sealed class StubPlayer { public int BottomDraws; public void ModDrawDsBottomScreen() => BottomDraws++; }
}
namespace MphRead.Mods.Network { public static class DemoPlayback { public static bool IsActive { get; set; } } }
namespace OpenTK.Graphics.OpenGL { public static class GL { public static void Viewport(int x,int y,int width,int height) { } } }
