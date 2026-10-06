#if MPHREAD_AVALONIA
using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.Launcher
{
    /// <summary>
    /// Minimal Supabase Realtime subscriber for private per-user social
    /// invalidations. Broadcast payloads are never trusted as social state;
    /// they only cause the authenticated Edge read model to refresh.
    /// </summary>
    internal static class SocialRealtimeClient
    {
        private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(20);

        public static async Task RunAsync(Action invalidated,
            CancellationToken cancellationToken)
        {
            int retrySeconds = 5;
            string lastFailure = "";
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await HunterLicenseClient.WithAuthenticatedSessionAsync(
                        async (session, token) =>
                        {
                            await RunSessionAsync(session, invalidated, token)
                                .ConfigureAwait(false);
                            return true;
                        }, cancellationToken).ConfigureAwait(false);
                    retrySeconds = 5;
                    lastFailure = "";
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    string message = ex.Message.Trim();
                    if (message.Length == 0) message = ex.GetType().Name;
                    if (!String.Equals(lastFailure, message, StringComparison.Ordinal))
                    {
                        lastFailure = message;
                        Console.WriteLine("[social] realtime unavailable: " + message);
                    }
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(retrySeconds), cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    retrySeconds = Math.Min(60, retrySeconds * 2);
                }
            }
        }

        private static async Task RunSessionAsync(
            AuthenticatedSessionContext session, Action invalidated,
            CancellationToken cancellationToken)
        {
            using var socket = new ClientWebSocket();
            Uri websocket = RealtimeUri(session.Url, session.ApiKey);
            using var sessionLifetime =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (session.ExpiresAt > 0)
            {
                DateTimeOffset refreshAt = DateTimeOffset.FromUnixTimeSeconds(
                    session.ExpiresAt).AddMinutes(-2);
                TimeSpan untilRefresh = refreshAt - DateTimeOffset.UtcNow;
                if (untilRefresh <= TimeSpan.Zero)
                    untilRefresh = TimeSpan.FromSeconds(5);
                sessionLifetime.CancelAfter(untilRefresh);
            }
            CancellationToken token = sessionLifetime.Token;

            await socket.ConnectAsync(websocket, token).ConfigureAwait(false);
            string topic = "realtime:social:user:" + session.UserId;
            long reference = 1;
            await SendAsync(socket, new
            {
                topic,
                @event = "phx_join",
                payload = new
                {
                    config = new
                    {
                        broadcast = new { ack = false, self = false },
                        presence = new { enabled = false },
                        postgres_changes = Array.Empty<object>(),
                        @private = true
                    },
                    access_token = session.AccessToken
                },
                @ref = "1",
                join_ref = "1"
            }, token).ConfigureAwait(false);

            bool joined = false;
            DateTimeOffset nextHeartbeat = DateTimeOffset.UtcNow + HeartbeatInterval;
            Task<string?>? receive = null;
            while (!token.IsCancellationRequested
                && socket.State == WebSocketState.Open)
            {
                receive ??= ReceiveTextAsync(socket, token);
                TimeSpan wait = nextHeartbeat - DateTimeOffset.UtcNow;
                if (wait < TimeSpan.Zero) wait = TimeSpan.Zero;
                Task delay = Task.Delay(wait, token);
                Task completed = await Task.WhenAny(receive, delay).ConfigureAwait(false);

                if (completed == delay)
                {
                    string heartbeatRef = (++reference).ToString(
                        System.Globalization.CultureInfo.InvariantCulture);
                    await SendAsync(socket, new
                    {
                        topic = "phoenix",
                        @event = "heartbeat",
                        payload = new { },
                        @ref = heartbeatRef,
                        join_ref = (string?)null
                    }, token).ConfigureAwait(false);
                    nextHeartbeat = DateTimeOffset.UtcNow + HeartbeatInterval;
                    continue;
                }

                string? text = await receive.ConfigureAwait(false);
                receive = null;
                if (text == null)
                    throw new InvalidOperationException("Supabase Realtime closed the social channel.");

                using JsonDocument document = JsonDocument.Parse(text);
                JsonElement root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                    continue;

                string @event = root.TryGetProperty("event", out JsonElement eventValue)
                    && eventValue.ValueKind == JsonValueKind.String
                        ? eventValue.GetString() ?? "" : "";
                string messageTopic = root.TryGetProperty("topic", out JsonElement topicValue)
                    && topicValue.ValueKind == JsonValueKind.String
                        ? topicValue.GetString() ?? "" : "";
                string messageRef = root.TryGetProperty("ref", out JsonElement refValue)
                    && refValue.ValueKind == JsonValueKind.String
                        ? refValue.GetString() ?? "" : "";

                if (@event == "phx_reply" && messageRef == "1")
                {
                    if (!root.TryGetProperty("payload", out JsonElement payload)
                        || !payload.TryGetProperty("status", out JsonElement status)
                        || status.GetString() != "ok")
                    {
                        throw new InvalidOperationException(
                            "Supabase Realtime rejected the private social channel.");
                    }
                    joined = true;
                    continue;
                }
                if (@event is "phx_error" or "phx_close")
                    throw new InvalidOperationException(
                        "Supabase Realtime closed the private social channel.");
                if (!joined || @event != "broadcast"
                    || !String.Equals(messageTopic, topic, StringComparison.Ordinal))
                    continue;

                if (root.TryGetProperty("payload", out JsonElement broadcast)
                    && broadcast.ValueKind == JsonValueKind.Object
                    && broadcast.TryGetProperty("event", out JsonElement name)
                    && name.ValueKind == JsonValueKind.String
                    && name.GetString() == "social_changed")
                {
                    invalidated();
                }
            }

            if (!cancellationToken.IsCancellationRequested
                && !sessionLifetime.IsCancellationRequested)
                throw new InvalidOperationException("Supabase Realtime disconnected.");
        }

        private static Uri RealtimeUri(string baseUrl, string apiKey)
        {
            var source = new Uri(baseUrl);
            var builder = new UriBuilder(source)
            {
                Scheme = source.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase)
                    ? "ws" : "wss",
                Port = source.IsDefaultPort ? -1 : source.Port,
                Path = "/realtime/v1/websocket",
                Query = "apikey=" + Uri.EscapeDataString(apiKey) + "&vsn=1.0.0"
            };
            return builder.Uri;
        }

        private static async Task SendAsync(
            ClientWebSocket socket, object value, CancellationToken cancellationToken)
        {
            byte[] data = JsonSerializer.SerializeToUtf8Bytes(value);
            await socket.SendAsync(new ArraySegment<byte>(data),
                WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
        }

        private static async Task<string?> ReceiveTextAsync(
            ClientWebSocket socket, CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[8192];
            using var stream = new MemoryStream();
            while (true)
            {
                WebSocketReceiveResult result = await socket.ReceiveAsync(
                    new ArraySegment<byte>(buffer), cancellationToken).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                    return null;
                if (result.MessageType != WebSocketMessageType.Text)
                    continue;
                if (result.Count > 0)
                    stream.Write(buffer, 0, result.Count);
                if (!result.EndOfMessage)
                {
                    if (stream.Length > 64 * 1024)
                        throw new InvalidDataException("Supabase Realtime social message exceeded 64 KiB.");
                    continue;
                }
                return Encoding.UTF8.GetString(stream.GetBuffer(), 0, checked((int)stream.Length));
            }
        }
    }

    internal readonly record struct AuthenticatedSessionContext(
        string Url,
        string ApiKey,
        string AccessToken,
        string UserId,
        long ExpiresAt);
}
#endif
