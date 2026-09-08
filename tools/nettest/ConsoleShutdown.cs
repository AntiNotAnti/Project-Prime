using System;
using System.Threading;

namespace MphRead.NetTest;

/// <summary>Connects Ctrl+C to a caller-owned cancellation source for long-running fixtures.</summary>
internal sealed class ConsoleShutdown : IDisposable
{
    private readonly CancellationTokenSource _cancellation;
    private readonly ConsoleCancelEventHandler _handler;

    public ConsoleShutdown(CancellationTokenSource cancellation)
    {
        _cancellation = cancellation;
        _handler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            _cancellation.Cancel();
        };
        Console.CancelKeyPress += _handler;
    }

    public void Dispose() => Console.CancelKeyPress -= _handler;
}
