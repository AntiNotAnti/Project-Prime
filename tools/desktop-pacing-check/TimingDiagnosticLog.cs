namespace MphRead.Mods;

// FrameTiming's diagnostic output is the only application dependency needed by
// this isolated check. All pacing policy and clock constants come from production sources.
internal static class DebugLog
{
    public static void Line(string category, string message)
        => Console.WriteLine($"FRAMETIMING [{category}] {message}");
}
