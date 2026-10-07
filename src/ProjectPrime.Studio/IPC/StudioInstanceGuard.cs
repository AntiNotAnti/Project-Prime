using System.Diagnostics;
using ProjectPrime.Studio.Protocol;

namespace ProjectPrime.Studio.IPC;

/// <summary>One Studio owner per installation/user; subsequent launches authenticate and forward.</summary>
public sealed class StudioInstanceGuard : IAsyncDisposable
{
    private FileStream? _lease;
    private StudioIpcServer? _server;
    private readonly string? _descriptorPath;
    private int _disposed;

    public bool IsPrimary { get; }
    public StudioRequestResult ForwardResult { get; }
    public StudioEndpointDescriptor? Endpoint { get; }

    private StudioInstanceGuard(StudioRequestResult result)
    { ForwardResult = result; }

    private StudioInstanceGuard(FileStream lease, StudioIpcServer server, StudioEndpointDescriptor endpoint, string descriptorPath)
    {
        IsPrimary = true;
        ForwardResult = StudioRequestResult.Success;
        _lease = lease;
        _server = server;
        Endpoint = endpoint;
        _descriptorPath = descriptorPath;
    }

    public static async Task<StudioInstanceGuard> TryAcquireAsync(string installationDirectory, string userDataDirectory,
        StudioOpenRequest initialRequest, Func<StudioOpenRequest, CancellationToken, Task<StudioRequestResult>> onRequest,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(onRequest);
        ArgumentNullException.ThrowIfNull(initialRequest);
        string? inheritedSecret = Environment.GetEnvironmentVariable("PROJECT_PRIME_STUDIO_LAUNCH_SECRET");
        Environment.SetEnvironmentVariable("PROJECT_PRIME_STUDIO_LAUNCH_SECRET", null);
        if (inheritedSecret != null)
        {
            try { _ = StudioIpcAuthentication.CreateProof(inheritedSecret, StudioIpcAuthentication.NewNonce(), "client", StudioProtocol.StudioIpcVersion); }
            catch (StudioProtocolException) { return new(StudioRequestResult.Rejected("The inherited Studio IPC capability is invalid.")); }
        }
        string? invalid = initialRequest.Validate();
        if (invalid != null) return new(StudioRequestResult.Rejected(invalid));
        string directory = StudioEndpointStore.GetDirectory(installationDirectory, userDataDirectory);
        try
        {
            StudioEndpointStore.EnsurePrivateDirectory(Path.GetDirectoryName(directory)!);
            StudioEndpointStore.EnsurePrivateDirectory(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { return new(StudioRequestResult.Rejected("Studio could not create its private IPC directory.")); }
        var deadline = Stopwatch.StartNew();
        StudioRequestResult? unavailable = null;
        while (deadline.Elapsed < TimeSpan.FromSeconds(8))
        {
            cancellationToken.ThrowIfCancellationRequested();
            FileStream? lease = null;
            try { lease = StudioEndpointStore.OpenLock(directory); }
            catch (IOException) { }
            catch (UnauthorizedAccessException)
            { return new(StudioRequestResult.Rejected("Studio could not access its private instance lock.")); }
            if (lease != null)
            {
                StudioIpcServer? server = null;
                try
                {
                    var endpoint = new StudioEndpointDescriptor(StudioProtocol.StudioIpcVersion,
                        // macOS's TMPDIR is long and Unix socket paths are limited to 104 bytes.
                        // Installation identity belongs in the descriptor path, not the pipe name.
                        "ProjectPrime.Studio." + Guid.NewGuid().ToString("N")[..16],
                        inheritedSecret ?? StudioIpcAuthentication.NewSecret(), Environment.ProcessId);
                    server = new StudioIpcServer(endpoint, onRequest);
                    StudioEndpointStore.Write(directory, endpoint);
                    server.Start();
                    return new(lease, server, endpoint, Path.Combine(directory, "endpoint.json"));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                    or PlatformNotSupportedException)
                {
                    if (server != null) await server.DisposeAsync().ConfigureAwait(false);
                    lease.Dispose();
                    return new(StudioRequestResult.Rejected("Studio could not start its authenticated local IPC endpoint."));
                }
            }
            try
            {
                var endpoint = StudioEndpointStore.Read(installationDirectory, userDataDirectory);
                var result = await StudioIpcClient.ForwardAsync(endpoint, initialRequest, cancellationToken).ConfigureAwait(false);
                if (!result.Accepted && (result.Error == StudioIpcClient.UnavailableMessage || result.Error == StudioIpcClient.TimeoutMessage)
                    && !cancellationToken.IsCancellationRequested)
                {
                    // A new primary may own the lock while replacing a crashed owner's descriptor.
                    // Re-read the endpoint and retry the same ID instead of dropping this launch.
                    unavailable = result;
                    await Task.Delay(50, cancellationToken).ConfigureAwait(false);
                    continue;
                }
                return new(result);
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            { await Task.Delay(50, cancellationToken).ConfigureAwait(false); }
            catch (Exception ex) when (ex is StudioProtocolException or UnauthorizedAccessException)
            { return new(StudioRequestResult.Rejected(ex.Message)); }
            catch (IOException)
            { await Task.Delay(50, cancellationToken).ConfigureAwait(false); }
        }
        return new(unavailable ?? StudioRequestResult.Rejected("The existing Studio did not publish its IPC endpoint before the startup timeout."));
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        if (_server != null) await _server.DisposeAsync().ConfigureAwait(false);
        if (_descriptorPath != null)
        {
            try { File.Delete(_descriptorPath); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        _lease?.Dispose();
        _lease = null;
        _server = null;
    }
}
