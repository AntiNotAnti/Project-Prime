namespace ProjectPrime.Studio.Settings;

/// <summary>Studio-owned paths. None resolve into the game's generated runtime directories.</summary>
public sealed record StudioPaths(string InstallationDirectory, string UserDataDirectory)
{
    public string SettingsFile => System.IO.Path.Combine(UserDataDirectory, "settings.json");
    public string SessionFile => System.IO.Path.Combine(UserDataDirectory, "session.json");
    public string LogFile => System.IO.Path.Combine(UserDataDirectory, "studio.log");
    public string BuildCacheDirectory => System.IO.Path.Combine(UserDataDirectory, "build-cache");
    public string StagingDirectory => System.IO.Path.Combine(UserDataDirectory, "staging");
    public string PlaytestDirectory => System.IO.Path.Combine(UserDataDirectory, "playtest");

    public static StudioPaths CreateDefault()
    {
        if (Environment.GetEnvironmentVariable("PROJECT_PRIME_STUDIO_USER_DATA") is { Length: > 0 } isolated)
        {
            if (!System.IO.Path.IsPathFullyQualified(isolated))
                throw new ArgumentException("PROJECT_PRIME_STUDIO_USER_DATA must be an absolute directory.");
            return new StudioPaths(AppContext.BaseDirectory, System.IO.Path.GetFullPath(isolated));
        }
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string root = OperatingSystem.IsMacOS()
            ? System.IO.Path.Combine(home, "Library", "Application Support")
            : OperatingSystem.IsWindows()
                ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
                : Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } xdg && System.IO.Path.IsPathFullyQualified(xdg)
                    ? xdg : System.IO.Path.Combine(home, ".local", "share");
        return new StudioPaths(AppContext.BaseDirectory, System.IO.Path.Combine(root, "ProjectPrime", "studio"));
    }
}
