// Asset-free dependency seams only; the capture/profiler state machine is linked
// unchanged from production. No GL calls, game scenes, or native windows run.
namespace OpenTK.Graphics.OpenGL
{
    internal enum StringName { Vendor, Renderer, Version }
}
namespace MphRead
{
    internal sealed class Scene
    {
        internal ulong FrameCount { get; set; } = 240;
        internal Room? Room => null;
        internal (int X, int Y) Size => (1920, 1080);
    }
    internal sealed class Room { internal (string Name, int Unused) Meta => ("test", 0); }
}
namespace MphRead.Entities
{
    internal static class PlayerEntity { internal static int PlayerCount => 1; }
}
namespace MphRead.Mods.Diagnostics
{
    internal static class RuntimeApplicationImage
    {
        internal static (string Sha256, string Kind) Capture(System.Reflection.Assembly assembly) => ("stub", "stub");
    }
}
namespace MphRead.Mods { internal static class DebugLog { internal static void Line(string category, string message) { } } }
namespace MphRead.Mods.Render
{
    internal enum OpenGlCpuPass { Ui, TextureUpload, Preparation, DrawItems, World, ItemSetup, GeometrySubmission, Shadow, Hud, Composite }
    internal enum OpenGlFrameCounter { TextureUploadBytes, UploadBytes, BufferOrphans }
    internal static class OpenGlPassProfiler
    {
        internal static void BeginFrame() { }
        internal static void EndFrame() { }
        internal static void CancelFrame() { }
        internal static double FrameMilliseconds(OpenGlCpuPass pass) => 0;
        internal static long FrameCounter(OpenGlFrameCounter counter) => 0;
    }
    internal static class GraphicsApi { internal static string GetString(OpenTK.Graphics.OpenGL.StringName name) => "stub"; }
    internal static class RenderOptions { internal static int ResolutionScale => 100; internal static object Shadows => "Off"; }
    internal static class AnimationRollout { internal static bool Enabled(string name) => false; }
    internal static class DesktopRetainedGeometry { internal static bool Enabled => false; internal static bool BatchEnabled => false; }
    internal static class OpenGlScopedBindingCache { internal static bool Enabled => false; }
    internal static class OpenGlContextPolicy { internal static object VaoApi => "stub"; internal static bool Vao => false; }
    internal static class OpenGlOptimizationPolicy { internal static bool Enabled(string name) => false; }
    internal static class HudSamplerCache { internal static bool Enabled => false; }
    internal static class MatrixPaletteRevision { internal static bool Enabled => false; }
    internal static class OpenGlSubmissionProfiler { internal static int PercentileIndex(int count, int percentile) => Math.Clamp((int)Math.Ceiling(count * percentile / 100.0) - 1, 0, count - 1); }
}
