namespace MphRead.Mods;
internal static class DebugLog
{
    internal static void Line(string category, string message) => Console.WriteLine("DIAGNOSTIC: " + message);
}
