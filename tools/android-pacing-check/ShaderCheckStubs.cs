namespace MphRead
{
    public sealed class ProgramException(string message) : System.Exception(message) { }
}
namespace MphRead.Mods.Render
{
    // The shader selection flag is orthogonal to source translation; tests pass
    // explicit variant sources and never need a current graphics context.
    internal static class OpenGlOptimizationPolicy
    {
        internal static bool Enabled(string name) => true;
    }
}
