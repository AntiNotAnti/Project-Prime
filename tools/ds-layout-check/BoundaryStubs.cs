namespace MphRead.Mods.Input
{
    // Only the input-mode owner and diagnostic counters are outside this
    // geometry fixture. All bounds, button geometry and hit tests are real.
    public static class PointerInput
    {
        public static bool StylusMode { get; set; }
        public static int JumpsIgnored => 0;
    }
}
namespace MphRead.Mods
{
    public static class DebugLog
    {
        public static bool Active => false;
        public static void Line(string category,string text) { }
    }
}
