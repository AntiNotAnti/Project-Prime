using System;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Admin;

public sealed record ServerAdminOptions(int Port, string KeySha256)
{
    public static ServerAdminOptions? FromEnvironment()
    {
        string? port = Environment.GetEnvironmentVariable("PRIME_SERVER_ADMIN_PORT");
        string? hash = Environment.GetEnvironmentVariable("PRIME_SERVER_ADMIN_KEY_SHA256");
        if (port == null && hash == null) return null;
        if (!int.TryParse(port, out int number) || number is < 1024 or > 65535 || hash?.Length != 64)
            throw new ArgumentException("Admin requires an explicit unprivileged loopback port and SHA256 credential hash.");
        try { _ = Convert.FromHexString(hash); } catch (FormatException) { throw new ArgumentException("Invalid admin credential hash."); }
        return new(number, hash);
    }
}

/// <summary>Loopback management only. Remote operators use an authenticated tunnel;
/// this endpoint never binds a public interface or mutates simulation objects.</summary>
public sealed class AdminHttpServer : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly AdminCommandQueue _commands;
    private readonly Func<object> _status;
    private readonly byte[] _keyHash;
    private readonly Task _worker;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    public AdminHttpServer(ServerAdminOptions options, AdminCommandQueue commands, Func<object> status)
    {
        if (options.Port is < 1024 or > 65535) throw new ArgumentException("Invalid admin port.");
        _keyHash = Convert.FromHexString(options.KeySha256);
        if (_keyHash.Length != 32) throw new ArgumentException("Invalid admin credential hash.");
        _commands = commands; _status = status;
        _listener.Prefixes.Add($"http://127.0.0.1:{options.Port}/");
        _listener.Start(); _worker = Task.Run(RunAsync);
    }

    private async Task RunAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            HttpListenerContext context;
            try { context = await _listener.GetContextAsync().WaitAsync(_stop.Token); }
            catch (Exception e) when (e is OperationCanceledException or HttpListenerException or ObjectDisposedException) { break; }
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
            deadline.CancelAfter(TimeSpan.FromSeconds(5));
            try { await HandleAsync(context, deadline.Token); }
            catch (Exception e) when (e is JsonException or FormatException or ArgumentException)
            { context.Response.StatusCode = 400; }
            catch (Exception e) when (e is IOException or HttpListenerException or OperationCanceledException) { }
            finally { context.Response.Close(); }
        }
    }

    private async Task HandleAsync(HttpListenerContext context, CancellationToken ct)
    {
        context.Response.Headers["Cache-Control"] = "no-store";
        string? authorization = context.Request.Headers["Authorization"];
        if (authorization == null || !authorization.StartsWith("Bearer ", StringComparison.Ordinal)
            || authorization.Length is < 39 or > 519
            || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(authorization[7..])), _keyHash))
        { context.Response.StatusCode = 401; return; }
        string path = context.Request.Url!.AbsolutePath;
        object? response;
        if (context.Request.HttpMethod == "GET" && path == "/v1/admin/status") response = _status();
        else if (context.Request.HttpMethod == "GET" && path.StartsWith("/v1/admin/commands/", StringComparison.Ordinal)
            && Guid.TryParseExact(path[19..], "D", out Guid request))
        {
            response = _commands.Find(request);
            if (response == null) { context.Response.StatusCode = 404; return; }
        }
        else if (context.Request.HttpMethod == "POST" && path == "/v1/admin/commands")
        {
            if (context.Request.ContentLength64 > 4096) { context.Response.StatusCode = 413; return; }
            using var body = new MemoryStream(); byte[] buffer = new byte[1024];
            while (true)
            {
                int count = await context.Request.InputStream.ReadAsync(buffer, ct);
                if (count == 0) break;
                if (body.Length + count > 4096) { context.Response.StatusCode = 413; return; }
                body.Write(buffer, 0, count);
            }
            var command = JsonSerializer.Deserialize<AdminCommand>(body.ToArray(), Json);
            if (command == null) { context.Response.StatusCode = 400; return; }
            var result = _commands.Submit(command); response = result;
            context.Response.StatusCode = result.State == "queued" ? 202 : result.State == "conflict" ? 409
                : result.State == "unavailable" ? 503 : result.State is "expired" or "rejected" ? 400 : 200;
        }
        else { context.Response.StatusCode = 404; return; }
        context.Response.ContentType = "application/json";
        await JsonSerializer.SerializeAsync(context.Response.OutputStream, response, Json, ct);
    }

    public void Dispose()
    {
        _stop.Cancel(); _listener.Stop();
        try { _worker.GetAwaiter().GetResult(); } finally { _listener.Close(); _stop.Dispose(); }
    }
}
