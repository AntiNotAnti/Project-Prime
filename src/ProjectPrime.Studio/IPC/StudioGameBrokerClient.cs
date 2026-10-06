using System.Diagnostics;
using System.Reflection;
using ProjectPrime.Studio.Protocol;

namespace ProjectPrime.Studio.IPC;

/// <summary>Hands immutable packages to the game. Studio never owns publication into active game runtime paths.</summary>
public sealed class StudioGameBrokerClient : IDisposable
{
    private readonly string _installationDirectory;
    private readonly string _userDataDirectory;
    private readonly string? _gameExecutable;
    private readonly string _studioVersion;
    private readonly SemaphoreSlim _launchGate = new(1);

    public StudioGameBrokerClient(string installationDirectory, string userDataDirectory, string? studioVersion = null)
    {
        _gameExecutable = FindGameExecutable(Path.GetFullPath(installationDirectory));
        _installationDirectory = _gameExecutable == null ? Path.GetFullPath(installationDirectory) : Path.GetDirectoryName(_gameExecutable)!;
        _userDataDirectory = Path.GetFullPath(userDataDirectory);
        _studioVersion = studioVersion ?? typeof(StudioGameBrokerClient).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "local";
    }

    public void Dispose() => _launchGate.Dispose();
    public string? GameExecutable => _gameExecutable;
    public string GameInstallationDirectory => _installationDirectory;

    public Task<StudioGameResult> InstallMapPackageAsync(string packagePath, StudioMapIdentity identity, CancellationToken cancellationToken = default)
        => RequestAsync(new(Guid.NewGuid(), StudioGameCommand.InstallMapPackage, packagePath, identity), true, cancellationToken);
    public Task<StudioGameResult> RequestPlaytestAsync(string packagePath, StudioMapIdentity identity,
        StudioPlaytestOptions? options = null, CancellationToken cancellationToken = default)
        => RequestAsync(new(Guid.NewGuid(), StudioGameCommand.PlaytestMap, packagePath, identity, options), true, cancellationToken);
    public Task<StudioGameResult> RequestHostAsync(string packagePath, StudioMapIdentity identity,
        StudioPlaytestOptions? options = null, CancellationToken cancellationToken = default)
        => RequestAsync(new(Guid.NewGuid(), StudioGameCommand.HostMap, packagePath, identity, options), true, cancellationToken);
    public Task<StudioGameResult> RequestCommunityTicketAsync(bool refresh, CancellationToken cancellationToken = default)
        => RequestAsync(new(Guid.NewGuid(), StudioGameCommand.CommunityTicket, RefreshTicket: refresh), false, cancellationToken);
    public Task<StudioGameResult> GetDiagnosticsAsync(CancellationToken cancellationToken = default)
        => RequestAsync(new(Guid.NewGuid(), StudioGameCommand.Diagnostics), false, cancellationToken);
    public Task<StudioGameResult> GetPlaytestStatusAsync(Guid playtestId, CancellationToken cancellationToken = default)
        => RequestAsync(new(Guid.NewGuid(), StudioGameCommand.PlaytestStatus, PlaytestId: playtestId), false, cancellationToken);
    public Task<StudioGameResult> StopPlaytestAsync(Guid playtestId, CancellationToken cancellationToken = default)
        => RequestAsync(new(Guid.NewGuid(), StudioGameCommand.StopPlaytest, PlaytestId: playtestId), false, cancellationToken);

    public async Task<StudioGameResult> RequestAsync(StudioGameRequest request, bool startGameIfMissing,
        CancellationToken cancellationToken = default)
    {
        request = request with { StudioVersion = _studioVersion };
        if (request.Validate() is { } invalid) return StudioGameResult.Rejected(invalid);
        try
        {
            var endpoint = await FindEndpointAsync(startGameIfMissing, cancellationToken).ConfigureAwait(false);
            if (endpoint == null)
                return StudioGameResult.Rejected(startGameIfMissing
                    ? "Project Prime is unavailable. Install a compatible game beside Studio or set PROJECT_PRIME_GAME_PATH."
                    : request.Command == StudioGameCommand.CommunityTicket
                        ? "Sign in through Project Prime to publish Community maps. Start Project Prime to connect Studio."
                        : "Project Prime is not running or its local broker is unavailable.");
            var response = await LocalIpcClient.RequestAsync(endpoint,
                new(StudioProtocol.StudioIpcVersion, "Game", request.RequestId, GameRequest: request), cancellationToken).ConfigureAwait(false);
            if (response.GameResult == null)
            {
                // Re-read the rotated descriptor after peer death/restart; retry only the same request ID.
                if (response.Error == LocalIpcClient.UnavailableMessage && startGameIfMissing)
                {
                    endpoint = await FindEndpointAsync(true, cancellationToken, forceStart: true).ConfigureAwait(false);
                    if (endpoint != null) response = await LocalIpcClient.RequestAsync(endpoint,
                        new(StudioProtocol.StudioIpcVersion, "Game", request.RequestId, GameRequest: request), cancellationToken).ConfigureAwait(false);
                }
                if (response.GameResult == null) return StudioGameResult.Rejected(response.Error ?? "The game returned an invalid broker response.");
            }
            StudioGameResult result = response.GameResult;
            if (result.IpcVersion != StudioProtocol.StudioIpcVersion)
                return StudioGameResult.Rejected("Studio <-> ProjectPrime version mismatch.");
            if (result.Accepted && request.Identity != null && !Matches(result.Identity, request.Identity))
                return StudioGameResult.Rejected("The game returned a different map package identity.");
            if (result.CommunityTicket != null && (request.Command != StudioGameCommand.CommunityTicket
                || !result.Accepted || result.CommunityTicket is not { Length: > 5 and < 8192 }
                || !result.CommunityTicket.StartsWith("ppm1.", StringComparison.Ordinal)))
                return StudioGameResult.Rejected("The game did not return a narrow Community map ticket.");
            if (result.Accepted && request.Command == StudioGameCommand.CommunityTicket && result.CommunityTicket == null)
                return StudioGameResult.Rejected("The game returned no Community map ticket.");
            if (result.Accepted && request.Command is StudioGameCommand.PlaytestStatus or StudioGameCommand.StopPlaytest
                && result.PlaytestId != request.PlaytestId)
                return StudioGameResult.Rejected("The game returned a stale playtest response.");
            return result;
        }
        catch (OperationCanceledException) { return StudioGameResult.Rejected("The game broker request was cancelled."); }
        catch (StudioProtocolException ex) { return StudioGameResult.Rejected(ex.Message); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        { return StudioGameResult.Rejected("Project Prime is unavailable: " + ex.Message); }
    }

    private async Task<StudioEndpointDescriptor?> FindEndpointAsync(bool start, CancellationToken token, bool forceStart = false)
    {
        StudioEndpointDescriptor? endpoint = null;
        try { endpoint = LocalIpcEndpointStore.Read(_installationDirectory, _userDataDirectory, StudioEndpointRole.Game); }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { }
        if (endpoint != null && !forceStart) return endpoint;
        if (!start || _gameExecutable == null) return null;
        await _launchGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            try
            {
                var current = LocalIpcEndpointStore.Read(_installationDirectory, _userDataDirectory, StudioEndpointRole.Game);
                if (!forceStart || endpoint == null || current.Secret != endpoint.Secret) return current;
                endpoint = current;
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { }
            // A live process ID does not mean its old pipe still accepts requests during shutdown.
            // A recovered old broker can avoid a duplicate game launch only after authenticating.
            if (forceStart && endpoint != null && await IsResponsiveAsync(endpoint, token).ConfigureAwait(false)) return endpoint;
            var launch = new ProcessStartInfo { UseShellExecute = false, WorkingDirectory = _installationDirectory };
            if (Path.GetExtension(_gameExecutable).Equals(".dll", StringComparison.OrdinalIgnoreCase))
            {
                string? runtime = Environment.GetEnvironmentVariable("DOTNET_ROOT");
                launch.FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")
                    ?? (runtime == null ? "dotnet" : Path.Combine(runtime, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"));
                launch.ArgumentList.Add(_gameExecutable);
            }
            else launch.FileName = _gameExecutable;
            launch.Environment["PROJECT_PRIME_STUDIO_LAUNCH_SECRET"] = StudioIpcAuthentication.NewSecret();
            launch.Environment["PROJECT_PRIME_STUDIO_IPC_DATA"] = _userDataDirectory;
            using Process? process = Process.Start(launch);
            if (process == null) return null;
            var deadline = Stopwatch.StartNew();
            while (deadline.Elapsed < TimeSpan.FromSeconds(12))
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var next = LocalIpcEndpointStore.Read(_installationDirectory, _userDataDirectory, StudioEndpointRole.Game);
                    if (endpoint == null || next.Secret != endpoint.Secret
                        || await IsResponsiveAsync(next, token).ConfigureAwait(false)) return next;
                }
                catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { }
                if (process.HasExited && process.ExitCode != 0) return null;
                await Task.Delay(50, token).ConfigureAwait(false);
            }
            return null;
        }
        finally { _launchGate.Release(); }
    }

    private static async Task<bool> IsResponsiveAsync(StudioEndpointDescriptor endpoint, CancellationToken token)
    {
        using var probe = CancellationTokenSource.CreateLinkedTokenSource(token);
        probe.CancelAfter(TimeSpan.FromMilliseconds(500));
        var response = await LocalIpcClient.RequestAsync(endpoint,
            new(StudioProtocol.StudioIpcVersion, "Ping", Guid.NewGuid()), probe.Token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        return response.Result?.Accepted == true;
    }
    private static bool Matches(StudioMapIdentity? actual, StudioMapIdentity required)
        => actual != null && actual.MapId == required.MapId
            && string.Equals(actual.RoomKey, required.RoomKey, StringComparison.OrdinalIgnoreCase)
            && string.Equals(actual.ContentHash, required.ContentHash, StringComparison.OrdinalIgnoreCase)
            && string.Equals(actual.PackageHash, required.PackageHash, StringComparison.OrdinalIgnoreCase);

    private static string? FindGameExecutable(string installation)
    {
        if (Environment.GetEnvironmentVariable("PROJECT_PRIME_GAME_PATH") is { Length: > 0 } configured)
            return Path.IsPathFullyQualified(configured) && File.Exists(configured) ? configured : null;
        string name = OperatingSystem.IsWindows() ? "ProjectPrime.exe" : "ProjectPrime";
        foreach (string candidate in new[] { Path.Combine(installation, name), Path.Combine(installation, "ProjectPrime.dll") })
            if (File.Exists(candidate)) return candidate;
        var directory = new DirectoryInfo(installation);
        for (int depth = 0; depth < 8 && directory != null; depth++, directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "src", "MphRead", "MphRead.csproj")))
                foreach (string configuration in new[] { "Release", "Debug" })
                    foreach (string candidate in new[] { name, "ProjectPrime.dll" })
                    {
                        string path = Path.Combine(directory.FullName, "src", "MphRead", "bin", configuration, "net10.0", candidate);
                        if (File.Exists(path)) return path;
                    }
            if (OperatingSystem.IsMacOS())
            {
                string bundle = Path.Combine(directory.FullName, "Project Prime.app", "Contents", "MacOS", "ProjectPrime");
                if (File.Exists(bundle)) return bundle;
            }
        }
        return null;
    }
}
