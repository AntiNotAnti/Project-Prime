using System.Collections.Concurrent;
using System.IO.Pipes;
using ProjectPrime.Studio.Protocol;

namespace ProjectPrime.Studio.Protocol;

public sealed class LocalIpcServer : IAsyncDisposable
{
    private const int MaximumConnections = 8;
    private const int MaximumRememberedRequests = 512;
    private readonly StudioEndpointDescriptor _endpoint;
    private readonly Func<StudioIpcEnvelope, CancellationToken, Task<StudioIpcEnvelope>> _handler;
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _connections = new(MaximumConnections);
    private readonly ConcurrentDictionary<int, Task> _sessions = new();
    private readonly Dictionary<Guid, RequestEntry> _requests = new();
    private readonly object _requestGate = new();
    private readonly NamedPipeServerStream _firstListener;
    private Task? _acceptLoop;
    private int _sessionId;

    private sealed class RequestEntry(StudioIpcEnvelope request, CancellationTokenSource cancellation)
    {
        public StudioIpcEnvelope Request { get; } = request;
        public CancellationTokenSource Cancellation { get; } = cancellation;
        public Task<StudioIpcEnvelope> Result { get; set; } = null!;
    }

    public LocalIpcServer(StudioEndpointDescriptor endpoint,
        Func<StudioIpcEnvelope, CancellationToken, Task<StudioIpcEnvelope>> handler)
    {
        _endpoint = endpoint;
        _handler = handler;
        // Create the actual endpoint before publishing its descriptor.
        _firstListener = CreateListener();
    }

    public void Start() => _acceptLoop = AcceptLoopAsync();

    private NamedPipeServerStream CreateListener() => new(_endpoint.PipeName, PipeDirection.InOut,
        MaximumConnections, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

    private async Task AcceptLoopAsync()
    {
        NamedPipeServerStream? listener = _firstListener;
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                await _connections.WaitAsync(_stop.Token).ConfigureAwait(false);
                listener ??= CreateListener();
                await listener.WaitForConnectionAsync(_stop.Token).ConfigureAwait(false);
                var connected = listener;
                listener = null;
                int id = Interlocked.Increment(ref _sessionId);
                Task task = ServeAndReleaseAsync(connected);
                _sessions[id] = task;
                _ = task.ContinueWith(completed =>
                {
                    _sessions.TryRemove(id, out _);
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException) { }
        finally { listener?.Dispose(); }
    }

    private async Task ServeAndReleaseAsync(NamedPipeServerStream pipe)
    {
        try { await ServeAsync(pipe).ConfigureAwait(false); }
        finally { _connections.Release(); }
    }

    private async Task ServeAsync(NamedPipeServerStream pipe)
    {
        using (pipe)
        using (var lifetime = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token))
        {
            lifetime.CancelAfter(StudioProtocol.HandshakeTimeout);
            Guid requestId = default;
            try
            {
                string nonce = StudioIpcAuthentication.NewNonce();
                await StudioIpcFraming.WriteAsync(pipe, new(StudioProtocol.StudioIpcVersion, "Challenge", Nonce: nonce), lifetime.Token).ConfigureAwait(false);
                var hello = await StudioIpcFraming.ReadAsync(pipe, lifetime.Token).ConfigureAwait(false);
                if (hello.Version != StudioProtocol.StudioIpcVersion)
                    throw new StudioProtocolException("Studio <-> ProjectPrime version mismatch.");
                if (hello.Type != "Hello" || !StudioIpcAuthentication.VerifyProof(_endpoint.Secret, nonce, "client", hello.Version, hello.Proof))
                    throw new StudioProtocolException("Studio IPC authentication failed.");
                await StudioIpcFraming.WriteAsync(pipe, new(StudioProtocol.StudioIpcVersion, "Welcome",
                    Proof: StudioIpcAuthentication.CreateProof(_endpoint.Secret, nonce, "server", StudioProtocol.StudioIpcVersion)), lifetime.Token).ConfigureAwait(false);
                lifetime.CancelAfter(_endpoint.Role == StudioEndpointRole.Game ? StudioProtocol.GameRequestTimeout : StudioProtocol.RequestTimeout);
                var envelope = await StudioIpcFraming.ReadAsync(pipe, lifetime.Token).ConfigureAwait(false);
                requestId = envelope.RequestId;
                if (envelope.Version != StudioProtocol.StudioIpcVersion)
                    throw new StudioProtocolException("Studio <-> ProjectPrime version mismatch.");
                if (envelope.Type == "Ping")
                {
                    await StudioIpcFraming.WriteAsync(pipe, new(StudioProtocol.StudioIpcVersion, "Result", requestId, Result: StudioRequestResult.Success), lifetime.Token).ConfigureAwait(false);
                    return;
                }
                string? invalid = ValidateRequest(envelope);
                if (invalid != null) throw new StudioProtocolException(invalid);
                RequestEntry entry = GetOrStartRequest(envelope);
                using var readCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                Task<StudioIpcEnvelope> cancellation = StudioIpcFraming.ReadAsync(pipe, readCancellation.Token);
                try
                {
                    if (await Task.WhenAny(entry.Result, cancellation).ConfigureAwait(false) == cancellation)
                    {
                        StudioIpcEnvelope cancel;
                        try { cancel = await cancellation.ConfigureAwait(false); }
                        catch { entry.Cancellation.Cancel(); throw; }
                        if (cancel.Version != StudioProtocol.StudioIpcVersion || cancel.Type != "Cancel" || cancel.RequestId != requestId)
                        {
                            entry.Cancellation.Cancel();
                            throw new StudioProtocolException("The Studio IPC cancellation is invalid.");
                        }
                        entry.Cancellation.Cancel();
                    }
                    StudioIpcEnvelope result;
                    try { result = await entry.Result.WaitAsync(lifetime.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException) { entry.Cancellation.Cancel(); throw; }
                    await StudioIpcFraming.WriteAsync(pipe, result, lifetime.Token).ConfigureAwait(false);
                }
                finally
                {
                    readCancellation.Cancel();
                    try { await cancellation.ConfigureAwait(false); }
                    catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException) { }
                }
            }
            catch (StudioProtocolException ex)
            {
                using var errorTimeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
                try { await StudioIpcFraming.WriteAsync(pipe, new(StudioProtocol.StudioIpcVersion, "Error", requestId, Error: ex.Message), errorTimeout.Token).ConfigureAwait(false); }
                catch (Exception failure) when (failure is IOException or OperationCanceledException or ObjectDisposedException) { }
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException) { }
        }
    }

    private static string? ValidateRequest(StudioIpcEnvelope request)
    {
        if (request.Type == "Open" && request.OpenRequest != null && request.OpenRequest.RequestId == request.RequestId)
            return request.OpenRequest.Validate();
        if (request.Type == "Game" && request.GameRequest != null && request.GameRequest.RequestId == request.RequestId)
            return request.GameRequest.Validate();
        return "The Studio IPC command is invalid or unsupported.";
    }

    private static StudioIpcEnvelope Failure(Guid requestId, string error)
        => new(StudioProtocol.StudioIpcVersion, "Result", requestId, Result: StudioRequestResult.Rejected(error),
            GameResult: StudioGameResult.Rejected(error));

    private RequestEntry GetOrStartRequest(StudioIpcEnvelope request)
    {
        lock (_requestGate)
        {
            if (_requests.TryGetValue(request.RequestId, out var existing))
            {
                if (existing.Request != request)
                    throw new StudioProtocolException("A duplicate Studio request ID contains a different request.");
                return existing;
            }
            if (_requests.Count >= MaximumRememberedRequests)
            {
                var completed = _requests.FirstOrDefault(pair => pair.Value.Result.IsCompleted);
                if (completed.Value == null) throw new StudioProtocolException("Studio has too many pending requests.");
                _requests.Remove(completed.Key);
                completed.Value.Cancellation.Dispose();
            }
            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
            var entry = new RequestEntry(request, cancellation);
            _requests.Add(request.RequestId, entry);
            entry.Result = RunRequestAsync(request, cancellation.Token);
            return entry;
        }
    }

    private async Task<StudioIpcEnvelope> RunRequestAsync(StudioIpcEnvelope request, CancellationToken cancellationToken)
    {
        try
        {
            // Run the callback away from the accept loop; UI owners explicitly marshal it.
            return await Task.Run(() => _handler(request, cancellationToken), cancellationToken)
                .ConfigureAwait(false)
                ?? Failure(request.RequestId, "The local broker returned no request result.");
        }
        catch (OperationCanceledException) { return Failure(request.RequestId, "The Studio request was cancelled."); }
        catch { return Failure(request.RequestId, "The local broker could not complete the request."); }
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        _firstListener.Dispose();
        if (_acceptLoop != null)
        {
            try { await _acceptLoop.ConfigureAwait(false); }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException) { }
        }
        Task sessions = Task.WhenAll(_sessions.Values);
        Task operations;
        lock (_requestGate) operations = Task.WhenAll(_requests.Values.Select(entry => entry.Result));
        Task drained = Task.WhenAll(sessions, operations);
        try { await drained.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); }
        catch (Exception ex) when (ex is TimeoutException or OperationCanceledException or IOException or ObjectDisposedException) { }
        if (drained.IsCompleted) Cleanup();
        else _ = drained.ContinueWith(_ => Cleanup(), CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private void Cleanup()
    {
        lock (_requestGate)
        {
            foreach (var entry in _requests.Values) entry.Cancellation.Dispose();
            _requests.Clear();
        }
        _connections.Dispose();
        _stop.Dispose();
    }
}
