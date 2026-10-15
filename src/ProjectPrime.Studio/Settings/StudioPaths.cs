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
        return new StudioPaths(AppContext.BaseDirectory, ProjectPrime.Studio.Protocol.LocalIpcEndpointStore.DefaultUserDataDirectory);
    }
}
