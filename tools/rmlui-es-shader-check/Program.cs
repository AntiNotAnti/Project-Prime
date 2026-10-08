MphRead.Mods.Render.EsShaders.CheckInSync();
System.Console.WriteLine("PASS all six desktop-to-ES shader source hashes (driver compilation is checked by the actual Android scene)");

namespace MphRead
{
    public sealed class ProgramException(string message) : System.Exception(message) { }
}
