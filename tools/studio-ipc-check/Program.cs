using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Pipes;
using System.Reflection;
using System.Text;
using ProjectPrime.Studio.IPC;
using ProjectPrime.Studio.Protocol;

if (args.FirstOrDefault() == "--child-owner")
{
    await using var child = await StudioInstanceGuard.TryAcquireAsync(args[1], args[2], NewRequest(),
        (request, token) =>
        {
            File.AppendAllText(args[3] + ".requests", request.RequestId.ToString() + "\n");
            return Task.FromResult(StudioRequestResult.Success);
        });
    if (!child.IsPrimary) return 2;
    await File.WriteAllTextAsync(args[3], "ready");
    await Task.Delay(Timeout.InfiniteTimeSpan);
    return 0;
}

string root = Path.Combine(Path.GetTempPath(), "project-prime-studio-ipc-check-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
string installation = Path.Combine(root, "installation");
string data = Path.Combine(root, "user-data");
int executions = 0;
var blockedStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var blockedCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var disconnectedStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var disconnectedCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
try
{
    string inheritedInstallation = Path.Combine(root, "inherited-installation"), inheritedData = Path.Combine(root, "inherited-data");
    string inheritedSecret = StudioIpcAuthentication.NewSecret();
    Environment.SetEnvironmentVariable("PROJECT_PRIME_STUDIO_LAUNCH_SECRET", inheritedSecret);
    await using (var inherited = await StudioInstanceGuard.TryAcquireAsync(inheritedInstallation, inheritedData, NewRequest(), (_, _) => Task.FromResult(StudioRequestResult.Success)))
        Require(inherited.IsPrimary && inherited.Endpoint!.Secret == inheritedSecret && Environment.GetEnvironmentVariable("PROJECT_PRIME_STUDIO_LAUNCH_SECRET") == null,
            "parent capability is consumed once without entering command-line arguments");
    Environment.SetEnvironmentVariable("PROJECT_PRIME_STUDIO_LAUNCH_SECRET", "malformed-parent-capability");
    await using (var invalidInherited = await StudioInstanceGuard.TryAcquireAsync(inheritedInstallation, inheritedData, NewRequest(), (_, _) => Task.FromResult(StudioRequestResult.Success)))
        Require(!invalidInherited.IsPrimary && !invalidInherited.ForwardResult.Accepted && Environment.GetEnvironmentVariable("PROJECT_PRIME_STUDIO_LAUNCH_SECRET") == null,
            "malformed inherited capability fails closed and is cleared");
    using (var missingGame = new StudioGameBrokerClient(Path.Combine(root, "missing-game"), Path.Combine(root, "missing-game-data")))
    {
        var diagnostics = await missingGame.GetDiagnosticsAsync();
        Require(!diagnostics.Accepted && diagnostics.Error == "Project Prime is not running or its local broker is unavailable.",
            "missing game diagnostics describe peer availability without sign-in or implicit launch");
        var ticket = await missingGame.RequestCommunityTicketAsync(false);
        Require(!ticket.Accepted && ticket.Error!.StartsWith("Sign in through Project Prime", StringComparison.Ordinal),
            "missing game ticket request keeps narrow sign-in guidance");
    }
    await using var owner = await StudioInstanceGuard.TryAcquireAsync(installation, data, NewRequest(), async (request, token) =>
    {
        Interlocked.Increment(ref executions);
        if (request.Path?.EndsWith(".blocked", StringComparison.Ordinal) == true)
        {
            blockedStarted.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, token); }
            finally { blockedCancelled.TrySetResult(); }
        }
        if (request.Path?.EndsWith(".disconnected", StringComparison.Ordinal) == true)
        {
            disconnectedStarted.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, token); }
            finally { disconnectedCancelled.TrySetResult(); }
        }
        return StudioRequestResult.Success;
    });
    Require(owner.IsPrimary, "primary process acquires durable instance lease");
    var endpoint = owner.Endpoint!;
    Require(endpoint.Version == 1, "Studio IPC version is independently 1");
    if (!OperatingSystem.IsWindows())
    {
        Require(File.GetUnixFileMode(StudioEndpointStore.GetDescriptorPath(installation, data))
            == (UnixFileMode.UserRead | UnixFileMode.UserWrite), "descriptor is mode 0600");
        Require(File.GetUnixFileMode(StudioEndpointStore.GetDirectory(installation, data))
            == (UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute), "endpoint directory is mode 0700");
    }
    string descriptorPath = StudioEndpointStore.GetDescriptorPath(installation, data);
    foreach (string invalidDescriptor in new[]
    {
        "{\"version\":1,\"pipeName\":null,\"secret\":null,\"processId\":1}",
        "{\"version\":1,\"pipeName\":\"ProjectPrime.Studio.invalid\",\"secret\":null,\"processId\":1}",
        "{\"version\":1,\"pipeName\":\"ProjectPrime.Studio.invalid\",\"secret\":\"invalid\",\"processId\":1}",
        "{\"version\":1,\"unknownField\":true}"
    })
    {
        await File.WriteAllTextAsync(descriptorPath, invalidDescriptor);
        bool rejected = false;
        try { _ = StudioEndpointStore.Read(installation, data); }
        catch (StudioProtocolException) { rejected = true; }
        Require(rejected, "malformed/null endpoint descriptor fails closed");
    }
    StudioEndpointStore.Write(StudioEndpointStore.GetDirectory(installation, data), endpoint);
    var open = NewRequest(StudioOpenKind.Map, Path.Combine(root, "map.json"));
    var result = await StudioIpcClient.ForwardAsync(endpoint, open);
    Require(result.Accepted && executions == 1, "authenticated open reaches handler");
    Require((await StudioIpcClient.ForwardAsync(endpoint, open)).Accepted && executions == 1,
        "duplicate request ID returns prior response without executing twice");
    var duplicate = await StudioIpcClient.ForwardAsync(endpoint, open with { Path = Path.Combine(root, "other.json") });
    Require(!duplicate.Accepted && duplicate.Error!.Contains("duplicate", StringComparison.OrdinalIgnoreCase),
        "conflicting duplicate ID is rejected");
    await using (var secondary = await StudioInstanceGuard.TryAcquireAsync(installation, data, NewRequest(), (_, _) => throw new Exception("secondary must not own endpoint")))
        Require(!secondary.IsPrimary && secondary.ForwardResult.Accepted, "second bootstrap forwards to existing owner");

    Require(!(await StudioIpcClient.ForwardAsync(endpoint with { Secret = StudioIpcAuthentication.NewSecret() }, NewRequest())).Accepted,
        "wrong authentication secret is rejected");
    await using (var wrongVersion = await ConnectRaw(endpoint))
    {
        _ = await StudioIpcFraming.ReadAsync(wrongVersion);
        await StudioIpcFraming.WriteAsync(wrongVersion, new(99, "Hello", Proof: "invalid"));
        var response = await ReadBounded(wrongVersion);
        Require(response.Type == "Error" && response.Error!.Contains("version mismatch", StringComparison.OrdinalIgnoreCase),
            "incompatible handshake version receives explicit error");
    }
    foreach (int length in new[] { 0, -1, StudioProtocol.MaximumFrameBytes + 1, int.MaxValue })
    {
        await using var malformed = await Authenticate(endpoint);
        byte[] prefix = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(prefix, length);
        await malformed.WriteAsync(prefix);
        var response = await ReadBounded(malformed);
        Require(response.Type == "Error" && response.Error!.Contains("length", StringComparison.OrdinalIgnoreCase),
            $"invalid/oversized frame length {length} is rejected before body allocation");
    }
    await using (var malformed = await Authenticate(endpoint))
    {
        byte[] body = Encoding.UTF8.GetBytes("{ malformed json");
        byte[] prefix = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(prefix, body.Length);
        await malformed.WriteAsync(prefix);
        await malformed.WriteAsync(body);
        Require((await ReadBounded(malformed)).Type == "Error", "malformed JSON is rejected");
    }
    foreach (string invalidJson in new[]
    {
        "null", "{\"version\":1,\"type\":null}", "{\"version\":1,\"type\":\"Open\",\"openRequest\":null}",
        "{\"version\":1,\"type\":\"Ping\",\"unknownField\":true}"
    })
    {
        await using var malformed = await Authenticate(endpoint);
        byte[] body = Encoding.UTF8.GetBytes(invalidJson);
        byte[] prefix = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(prefix, body.Length);
        await malformed.WriteAsync(prefix);
        await malformed.WriteAsync(body);
        Require((await ReadBounded(malformed)).Type == "Error", "null/unknown wire fields are rejected without crashing listener");
    }
    await using (var invalid = await Authenticate(endpoint))
    {
        var relative = NewRequest(StudioOpenKind.Map, "relative.json");
        await StudioIpcFraming.WriteAsync(invalid, new(1, "Open", relative.RequestId, OpenRequest: relative));
        Require((await ReadBounded(invalid)).Type == "Error", "server validates request path independently of CLI");
    }
    await using (var invalid = await Authenticate(endpoint))
    {
        var unknownKind = NewRequest() with { Kind = (StudioOpenKind)99 };
        await StudioIpcFraming.WriteAsync(invalid, new(1, "Open", unknownKind.RequestId, OpenRequest: unknownKind));
        Require((await ReadBounded(invalid)).Type == "Error", "unsupported workspace kinds are rejected");
    }
    await using (var unsupported = await Authenticate(endpoint))
    {
        await StudioIpcFraming.WriteAsync(unsupported, new(1, "RequestMapPlaytest", Guid.NewGuid()));
        Require((await ReadBounded(unsupported)).Type == "Error", "unimplemented game broker commands receive explicit unsupported error");
    }
    await using (var invalid = await Authenticate(endpoint))
    {
        await StudioIpcFraming.WriteAsync(invalid, new(99, "Open", open.RequestId, OpenRequest: open));
        Require((await ReadBounded(invalid)).Error!.Contains("version mismatch", StringComparison.OrdinalIgnoreCase),
            "post-authentication message version is also checked");
    }
    await using (var fragmented = await Authenticate(endpoint))
    {
        var ping = new StudioIpcEnvelope(1, "Ping", Guid.NewGuid());
        using var bytes = new MemoryStream();
        await StudioIpcFraming.WriteAsync(bytes, ping);
        foreach (byte value in bytes.ToArray()) await fragmented.WriteAsync(new[] { value });
        Require((await ReadBounded(fragmented)).Result?.Accepted == true, "fragmented pipe frames are read exactly");
    }
    await using (var cancellation = await Authenticate(endpoint))
    {
        var request = NewRequest(StudioOpenKind.Map, Path.Combine(root, "map.blocked"));
        await StudioIpcFraming.WriteAsync(cancellation, new(1, "Open", request.RequestId, OpenRequest: request));
        await blockedStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await StudioIpcFraming.WriteAsync(cancellation, new(1, "Cancel", request.RequestId));
        Require((await ReadBounded(cancellation)).Result?.Accepted == false, "explicit cancellation returns rejected result");
        await blockedCancelled.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Require(true, "cancellation reaches background operation token");
    }
    var disconnected = await Authenticate(endpoint);
    var disconnectedRequest = NewRequest(StudioOpenKind.Map, Path.Combine(root, "map.disconnected"));
    await StudioIpcFraming.WriteAsync(disconnected, new(1, "Open", disconnectedRequest.RequestId, OpenRequest: disconnectedRequest));
    await disconnectedStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
    await disconnected.DisposeAsync();
    await disconnectedCancelled.Task.WaitAsync(TimeSpan.FromSeconds(3));
    Require(true, "client disappearance cancels outstanding handler");
    await using (var idle = await ConnectRaw(endpoint))
    {
        _ = await ReadBounded(idle);
        bool closed = false;
        try { _ = await ReadBounded(idle); }
        catch (EndOfStreamException) { closed = true; }
        catch (IOException) { closed = true; }
        Require(closed, "unauthenticated idle peer is disconnected by bounded timeout");
    }
    Require((await StudioIpcClient.ForwardAsync(endpoint, NewRequest())).Accepted, "malformed peers do not poison later connections");
    await owner.DisposeAsync();
    Require(!File.Exists(StudioEndpointStore.GetDescriptorPath(installation, data)), "clean shutdown removes endpoint descriptor");
    Require(!(await StudioIpcClient.ForwardAsync(endpoint, NewRequest())).Accepted, "Studio disappearance returns bounded unavailable result");
    using (var startupLease = StudioEndpointStore.OpenLock(StudioEndpointStore.GetDirectory(installation, data)))
    {
        StudioEndpointStore.Write(StudioEndpointStore.GetDirectory(installation, data), endpoint);
        Task<StudioInstanceGuard> waitingLaunch = StudioInstanceGuard.TryAcquireAsync(installation, data,
            NewRequest(), (_, _) => throw new Exception("waiting bootstrap must not own endpoint"));
        await Task.Delay(100);
        startupLease.Dispose();
        await using var startingOwner = await StudioInstanceGuard.TryAcquireAsync(installation, data,
            NewRequest(), (_, _) => Task.FromResult(StudioRequestResult.Success));
        Require(startingOwner.IsPrimary, "replacement primary owns lock during stale-descriptor startup race");
        await using var forwarded = await waitingLaunch.WaitAsync(TimeSpan.FromSeconds(8));
        Require(!forwarded.IsPrimary && forwarded.ForwardResult.Accepted,
            "simultaneous bootstrap retries stale descriptor and forwards to replacement primary");
    }
    await using (var restarted = await StudioInstanceGuard.TryAcquireAsync(installation, data, NewRequest(), (_, _) => Task.FromResult(StudioRequestResult.Success)))
    {
        Require(restarted.IsPrimary && restarted.Endpoint!.Secret != endpoint.Secret, "restart acquires same durable lock and rotates local capability");
        Require((await StudioIpcClient.ForwardAsync(restarted.Endpoint!, NewRequest())).Accepted, "client reconnects to restarted Studio");
        Require(!(await StudioIpcClient.ForwardAsync(restarted.Endpoint! with { Secret = endpoint.Secret }, NewRequest())).Accepted,
            "stale launch capability cannot authenticate a restarted owner");
    }
    await CheckSeparateProcesses(root);
    Console.WriteLine("Studio IPC checks passed.");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine("Studio IPC check failed: " + ex.GetType().Name + ": " + ex.Message);
    return 1;
}
finally
{
    try { Directory.Delete(root, true); }
    catch (IOException) { }
}

static StudioOpenRequest NewRequest(StudioOpenKind kind = StudioOpenKind.Home, string? path = null)
    => new(Guid.NewGuid(), kind, path);

static void Require(bool condition, string label)
{
    if (!condition) throw new InvalidOperationException(label);
    Console.WriteLine("PASS " + label);
}

static async Task<NamedPipeClientStream> ConnectRaw(StudioEndpointDescriptor endpoint)
{
    var pipe = new NamedPipeClientStream(".", endpoint.PipeName, PipeDirection.InOut,
        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
    await pipe.ConnectAsync(timeout.Token);
    return pipe;
}

static async Task<NamedPipeClientStream> Authenticate(StudioEndpointDescriptor endpoint)
{
    var pipe = await ConnectRaw(endpoint);
    var challenge = await ReadBounded(pipe);
    await StudioIpcFraming.WriteAsync(pipe, new(1, "Hello",
        Proof: StudioIpcAuthentication.CreateProof(endpoint.Secret, challenge.Nonce!, "client", 1)));
    var welcome = await ReadBounded(pipe);
    if (welcome.Type != "Welcome" || !StudioIpcAuthentication.VerifyProof(endpoint.Secret, challenge.Nonce!, "server", 1, welcome.Proof))
        throw new InvalidOperationException("mutual server authentication failed");
    return pipe;
}

static async Task<StudioIpcEnvelope> ReadBounded(Stream stream)
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
    return await StudioIpcFraming.ReadAsync(stream, timeout.Token);
}

static async Task CheckSeparateProcesses(string root)
{
    string installation = Path.Combine(root, "child-installation");
    string data = Path.Combine(root, "child-data");
    string ready = Path.Combine(root, "child-ready");
    string executable = Environment.ProcessPath!;
    var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
    if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
    foreach (string argument in new[] { "--child-owner", installation, data, ready }) start.ArgumentList.Add(argument);
    using var process = Process.Start(start) ?? throw new InvalidOperationException("child owner failed to start");
    try
    {
        var deadline = Stopwatch.StartNew();
        while (!File.Exists(ready) && deadline.Elapsed < TimeSpan.FromSeconds(8))
        {
            if (process.HasExited) throw new InvalidOperationException("child owner exited: " + await process.StandardError.ReadToEndAsync());
            await Task.Delay(25);
        }
        Require(File.Exists(ready), "independent Studio process owns endpoint");
        await using (var secondary = await StudioInstanceGuard.TryAcquireAsync(installation, data, NewRequest(), (_, _) => throw new Exception("duplicate owner")))
            Require(!secondary.IsPrimary && secondary.ForwardResult.Accepted && File.Exists(ready + ".requests"),
                "cross-process lease forwards document without spawning duplicate owner");
        process.Kill(true);
        await process.WaitForExitAsync();
        Require(File.Exists(StudioEndpointStore.GetDescriptorPath(installation, data)), "abrupt process termination leaves stale descriptor for recovery test");
        await using var replacement = await StudioInstanceGuard.TryAcquireAsync(installation, data, NewRequest(), (_, _) => Task.FromResult(StudioRequestResult.Success));
        Require(replacement.IsPrimary, "OS releases crashed process lease and next launch replaces stale descriptor");
        Require((await StudioIpcClient.ForwardAsync(replacement.Endpoint!, NewRequest())).Accepted,
            "recovered owner accepts authenticated request");
    }
    finally
    {
        if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); }
    }
}
