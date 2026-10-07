using ProjectPrime.Studio.Protocol;

namespace ProjectPrime.Studio;

public static class StudioLaunchRequest
{
    public const string Usage = "ProjectPrimeStudio [--map <project.json|package.ppmap> | --replay <file.ppdemo> | --clip <file.ppclip>] [--recover] [--safe-mode]";

    public static bool TryParse(string[] args, out StudioOpenRequest request, out string? error)
    {
        StudioOpenKind kind = StudioOpenKind.Home;
        string? path = null;
        bool recover = false, safeMode = false;
        error = null;
        for (int index = 0; index < args.Length; index++)
        {
            string arg = args[index];
            if (arg is "--recover") { recover = true; continue; }
            if (arg is "--safe-mode") { safeMode = true; continue; }
            if (arg is not ("--map" or "--replay" or "--clip")) { error = $"Unknown argument: {arg}"; break; }
            if (kind != StudioOpenKind.Home) { error = "Choose one document to open per launch."; break; }
            if (++index == args.Length || args[index].StartsWith("--", StringComparison.Ordinal)) { error = $"{arg} requires a file path."; break; }
            kind = arg == "--map" ? StudioOpenKind.Map : arg == "--replay" ? StudioOpenKind.Replay : StudioOpenKind.Clip;
            try { path = Path.GetFullPath(args[index]); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { error = "Invalid file path: " + ex.Message; break; }
        }
        request = new StudioOpenRequest(Guid.NewGuid(), kind, path, recover, safeMode);
        error ??= request.Validate();
        return error is null;
    }
}
