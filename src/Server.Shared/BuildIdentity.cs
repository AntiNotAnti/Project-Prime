using System.Reflection;

namespace ProjectPrime.Server.Shared;

/// <summary>
/// The build identity used when a client and a Server Node negotiate
/// compatibility. Release builds use their three-part release version. The
/// SDK's implicit 1.0.0 (including its source suffix) is a local build and is
/// intentionally stable across separately published development binaries.
/// </summary>
public static class BuildIdentity
{
    private static readonly Lazy<Version?> CurrentVersion = new(ReadCurrent);

    public static Version? Current => CurrentVersion.Value;
    public static bool IsRelease => Current != null;
    public static string Display => Format(Current);

    public static string DisplayFor(string? informationalVersion) => Format(Parse(informationalVersion));

    public static Version? Parse(string? text)
    {
        if (String.IsNullOrWhiteSpace(text))
            return null;
        text = text.Trim();
        if (text.Length > 1 && (text[0] == 'v' || text[0] == 'V'))
            text = text[1..];
        int plus = text.IndexOf('+');
        if (plus >= 0)
            text = text[..plus];
        if (text.IndexOf('-') >= 0)
            return null;
        if (!Version.TryParse(text, out Version? version))
            return null;
        // 1.0.0 is the SDK's default when no release version was supplied.
        if (version.Major == 1 && version.Minor == 0 && version.Build <= 0)
            return null;
        return Normalise(version);
    }

    public static Version Normalise(Version version) => new(
        version.Major, version.Minor, version.Build < 0 ? 0 : version.Build);

    private static string Format(Version? version) => version == null ? "a local build" : "v" + version.ToString(3);

    private static Version? ReadCurrent()
    {
        Assembly? assembly = Assembly.GetEntryAssembly() ?? typeof(BuildIdentity).Assembly;
        string? text = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return Parse(text);
    }
}
