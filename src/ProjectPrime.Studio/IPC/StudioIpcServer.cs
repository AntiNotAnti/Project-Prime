using ProjectPrime.Studio.Protocol;

namespace ProjectPrime.Studio.IPC;

internal sealed class StudioIpcServer : IAsyncDisposable
{
    private readonly LocalIpcServer _server;
    internal StudioIpcServer(StudioEndpointDescriptor endpoint, Func<StudioOpenRequest, CancellationToken, Task<StudioRequestResult>> handler)
    {
        _server = new(endpoint, async (request, token) =>
        {
            if (request.Type != "Open" || request.OpenRequest == null)
                return new(StudioProtocol.StudioIpcVersion, "Error", request.RequestId, Error: "The Studio IPC command is invalid or unsupported.");
            StudioRequestResult result = await handler(request.OpenRequest, token).ConfigureAwait(false);
            return new(StudioProtocol.StudioIpcVersion, "Result", request.RequestId, Result: result);
        });
    }
    internal void Start() => _server.Start();
    public ValueTask DisposeAsync() => _server.DisposeAsync();
}
