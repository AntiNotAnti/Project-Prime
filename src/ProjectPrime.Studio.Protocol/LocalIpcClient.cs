using System.IO.Pipes;
using ProjectPrime.Studio.Protocol;

namespace ProjectPrime.Studio.Protocol;

/// <summary>Local named-pipe client. No network socket or game account credential is used.</summary>
public static class LocalIpcClient
{
    public const string UnavailableMessage = "Studio disconnected or is unavailable. Retry the request.";
    public const string TimeoutMessage = "Studio IPC timed out. The existing Studio may still be starting or busy.";

    private static StudioIpcEnvelope Failure(Guid requestId, string error)
        => new(StudioProtocol.StudioIpcVersion, "Error", requestId, Error: error);

    public static async Task<StudioIpcEnvelope> RequestAsync(StudioEndpointDescriptor endpoint,
        StudioIpcEnvelope request, CancellationToken cancellationToken = default)
    {
        string? invalid = request.Type == "Open" ? request.OpenRequest?.Validate() ?? (request.OpenRequest == null ? "Missing document request." : null)
            : request.Type == "Game" ? request.GameRequest?.Validate() ?? (request.GameRequest == null ? "Missing game request." : null)
            : request.Type == "Ping" ? null : "Unsupported local IPC command.";
        if (invalid != null) return Failure(request.RequestId, invalid);
        using var pipe = new NamedPipeClientStream(".", endpoint.PipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        connectTimeout.CancelAfter(StudioProtocol.ConnectTimeout);
        bool authenticated = false;
        try
        {
            await pipe.ConnectAsync(connectTimeout.Token).ConfigureAwait(false);
            using var handshakeTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            handshakeTimeout.CancelAfter(StudioProtocol.HandshakeTimeout);
            var challenge = await StudioIpcFraming.ReadAsync(pipe, handshakeTimeout.Token).ConfigureAwait(false);
            if (challenge.Version != StudioProtocol.StudioIpcVersion)
                return Failure(request.RequestId, "Studio <-> ProjectPrime version mismatch.");
            if (challenge.Type != "Challenge" || challenge.Nonce == null)
                return Failure(request.RequestId, "Studio IPC authentication failed.");
            await StudioIpcFraming.WriteAsync(pipe, new(StudioProtocol.StudioIpcVersion, "Hello",
                Proof: StudioIpcAuthentication.CreateProof(endpoint.Secret, challenge.Nonce, "client", StudioProtocol.StudioIpcVersion)),
                handshakeTimeout.Token).ConfigureAwait(false);
            var welcome = await StudioIpcFraming.ReadAsync(pipe, handshakeTimeout.Token).ConfigureAwait(false);
            if (welcome.Type == "Error") return Failure(request.RequestId, welcome.Error ?? "Studio IPC authentication failed.");
            if (welcome.Version != StudioProtocol.StudioIpcVersion || welcome.Type != "Welcome"
                || !StudioIpcAuthentication.VerifyProof(endpoint.Secret, challenge.Nonce, "server", welcome.Version, welcome.Proof))
                return Failure(request.RequestId, "Studio IPC authentication failed.");
            authenticated = true;

            using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            requestTimeout.CancelAfter(endpoint.Role == StudioEndpointRole.Game ? StudioProtocol.GameRequestTimeout : StudioProtocol.RequestTimeout);
            await StudioIpcFraming.WriteAsync(pipe, request, requestTimeout.Token).ConfigureAwait(false);
            try
            {
                var response = await StudioIpcFraming.ReadAsync(pipe, requestTimeout.Token).ConfigureAwait(false);
                if (response.Version != StudioProtocol.StudioIpcVersion)
                    return Failure(request.RequestId, "Studio <-> ProjectPrime version mismatch.");
                if (response.Type == "Error") return Failure(request.RequestId, response.Error ?? "Studio rejected the request.");
                if (response.Type != "Result" || response.RequestId != request.RequestId || (response.Result == null && response.GameResult == null))
                    return Failure(request.RequestId, "Studio returned an invalid IPC response.");
                return response;
            }
            catch (OperationCanceledException)
            {
                // Explicit cancellation is best effort; disposal also cancels disconnected requests.
                using var cancelTimeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
                try { await StudioIpcFraming.WriteAsync(pipe, new(StudioProtocol.StudioIpcVersion, "Cancel", request.RequestId), cancelTimeout.Token).ConfigureAwait(false); }
                catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException) { }
                throw;
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return Failure(request.RequestId, authenticated ? TimeoutMessage : UnavailableMessage); }
        catch (OperationCanceledException) { return Failure(request.RequestId, "The Studio request was cancelled."); }
        catch (StudioProtocolException ex) { return Failure(request.RequestId, ex.Message); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ObjectDisposedException)
        { return Failure(request.RequestId, UnavailableMessage); }
    }
}
