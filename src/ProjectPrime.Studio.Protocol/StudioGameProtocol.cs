namespace ProjectPrime.Studio.Protocol;

public enum StudioEndpointRole { Studio, Game }
public enum StudioGameCommand { Diagnostics, InstallMapPackage, PlaytestMap, HostMap, CommunityTicket, PlaytestStatus, StopPlaytest }
public enum StudioPlaytestState { None, Accepted, Started, Ended, Rejected }

public sealed record StudioMapIdentity(Guid MapId, string RoomKey, string ContentHash, string PackageHash)
{
    public string? Validate()
    {
        if (MapId == Guid.Empty || string.IsNullOrWhiteSpace(RoomKey) || RoomKey.Length > 128
            || RoomKey.Any(c => char.IsControl(c) || c is '/' or '\\')) return "The map identity is invalid.";
        if (!IsHash(ContentHash) || !IsHash(PackageHash)) return "An exact map content and package hash is required.";
        return null;
    }
    private static bool IsHash(string? hash) => hash?.Length == 64 && hash.All(char.IsAsciiHexDigit);
}

public sealed record StudioPlaytestOptions(int Hunter = 0, int Bots = 0, int BotLevel = 5, int Port = 27015,
    string? CommunityAddress = null);

public sealed record StudioGameRequest(Guid RequestId, StudioGameCommand Command, string? PackagePath = null,
    StudioMapIdentity? Identity = null, StudioPlaytestOptions? Options = null, Guid PlaytestId = default, bool RefreshTicket = false,
    string StudioVersion = "local")
{
    public string? Validate()
    {
        if (RequestId == Guid.Empty || !Enum.IsDefined(Command)) return "The game broker command is invalid.";
        if (string.IsNullOrEmpty(StudioVersion) || StudioVersion.Length > 128
            || StudioVersion.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '.' and not '-' and not '+' and not '_'))
            return "The Studio application version is invalid.";
        if (Command is StudioGameCommand.InstallMapPackage or StudioGameCommand.PlaytestMap or StudioGameCommand.HostMap)
        {
            if (string.IsNullOrWhiteSpace(PackagePath) || PackagePath.Length > StudioProtocol.MaximumPathCharacters
                || PackagePath.Contains('\0') || !Path.IsPathFullyQualified(PackagePath)
                || !Path.GetExtension(PackagePath).Equals(".ppmap", StringComparison.OrdinalIgnoreCase))
                return "An absolute immutable .ppmap package path is required.";
            if (Identity == null) return "An exact immutable map identity is required.";
            if (Identity.Validate() is { } error) return error;
            if (Options is { } options && (options.Hunter is < 0 or > 6 || options.Bots is < 0 or > 7
                || options.BotLevel is < 1 or > 5 || options.Port is < 1 or > 65535))
                return "The playtest options are invalid.";
            if (Options?.CommunityAddress is { } address && (address.Length > 512
                || !Uri.TryCreate(address, UriKind.Absolute, out var uri) || (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback))
                || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)))
                return "The Community map service address is invalid.";
        }
        if (Command is StudioGameCommand.PlaytestStatus or StudioGameCommand.StopPlaytest && PlaytestId == Guid.Empty)
            return "The playtest ID is missing.";
        return null;
    }
}

public sealed record StudioGameResult(bool Accepted, string? Error = null, bool Deferred = false,
    StudioMapIdentity? Identity = null, string? CommunityTicket = null, string? GameVersion = null,
    int IpcVersion = StudioProtocol.StudioIpcVersion, Guid PlaytestId = default,
    StudioPlaytestState PlaytestState = StudioPlaytestState.None, string? StudioVersion = null)
{
    public static StudioGameResult Rejected(string error, bool deferred = false)
        => new(false, error, deferred);
}
