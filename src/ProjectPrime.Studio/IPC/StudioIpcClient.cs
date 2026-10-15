using ProjectPrime.Studio.Protocol;

namespace ProjectPrime.Studio.IPC;

/// <summary>Studio adapter over the shared authenticated named-pipe client.</summary>
public static class StudioIpcClient
{
    internal const string UnavailableMessage = LocalIpcClient.UnavailableMessage;
    internal const string TimeoutMessage = LocalIpcClient.TimeoutMessage;
    public static async Task<StudioRequestResult> ForwardAsync(StudioEndpointDescriptor endpoint,
        StudioOpenRequest request, CancellationToken cancellationToken = default)
    {
        string? invalid = request.Validate();
        if (invalid != null) return StudioRequestResult.Rejected(invalid);
        var response = await LocalIpcClient.RequestAsync(endpoint,
            new(StudioProtocol.StudioIpcVersion, "Open", request.RequestId, OpenRequest: request), cancellationToken).ConfigureAwait(false);
        return response.Result ?? StudioRequestResult.Rejected(response.Error ?? "Studio returned no document result.");
    }
}
