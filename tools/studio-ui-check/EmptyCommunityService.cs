using System.Net;
using System.Net.Sockets;
using System.Text;

// A deterministic empty discovery response; it models no publishing/authentication behavior.
internal sealed class EmptyCommunityService : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Task _server;
    private int _requests;
    public int Requests => Volatile.Read(ref _requests);
    public string Address { get; }

    public EmptyCommunityService()
    {
        _listener.Start();
        Address = "http://127.0.0.1:" + ((IPEndPoint)_listener.LocalEndpoint).Port + "/";
        _server = Task.Run(ServeAsync);
    }

    private async Task ServeAsync()
    {
        try
        {
            while (!_lifetime.IsCancellationRequested)
            {
                using TcpClient connection = await _listener.AcceptTcpClientAsync(_lifetime.Token);
                await using NetworkStream stream = connection.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                while (await reader.ReadLineAsync(_lifetime.Token) is { Length: > 0 }) { }
                Interlocked.Increment(ref _requests);
                await stream.WriteAsync("HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: 2\r\nConnection: close\r\n\r\n[]"u8.ToArray(), _lifetime.Token);
                await stream.FlushAsync(_lifetime.Token);
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (SocketException) when (_lifetime.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (_lifetime.IsCancellationRequested) { }
    }

    public async ValueTask DisposeAsync()
    {
        await _lifetime.CancelAsync();
        _listener.Stop();
        await _server;
        _lifetime.Dispose();
    }
}
