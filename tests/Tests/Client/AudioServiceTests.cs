using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Sound;
using Xunit;

namespace MphRead.Tests.Client;

public sealed class AudioServiceTests
{
    [Fact]
    public async Task NativeLifetimeSerializesConcurrentOperations()
    {
        var lifetime = new AudioDeviceLifetime();
        int active = 0;
        int maximum = 0;

        Task[] operations = Enumerable.Range(0, 12).Select(_ => Task.Run(() =>
            lifetime.Execute(() =>
            {
                int now = Interlocked.Increment(ref active);
                int observed;
                do
                {
                    observed = maximum;
                    if (now <= observed) break;
                }
                while (Interlocked.CompareExchange(ref maximum, now, observed) != observed);

                Thread.Sleep(2);
                Interlocked.Decrement(ref active);
            }))).ToArray();

        await Task.WhenAll(operations);

        Assert.Equal(1, maximum);
        lifetime.Dispose();
    }

    [Fact]
    public void ShutdownRunsOnceAndRejectsLateCallbacks()
    {
        var lifetime = new AudioDeviceLifetime();
        int shutdowns = 0;

        lifetime.Shutdown(() => shutdowns++);
        lifetime.Shutdown(() => shutdowns++);

        Assert.Equal(1, shutdowns);
        Assert.False(lifetime.TryExecute(static () => { }));
    }

    [Fact]
    public void ShutdownAllowsReentrantCleanupBeforeClosingGate()
    {
        var lifetime = new AudioDeviceLifetime();
        int nestedCleanup = 0;

        lifetime.Shutdown(() =>
        {
            Assert.True(lifetime.TryExecute(() => nestedCleanup++));
        });

        Assert.Equal(1, nestedCleanup);
        Assert.False(lifetime.TryExecute(static () => { }));
    }

    [Fact]
    public void DisposeIsIdempotent()
    {
        var lifetime = new AudioDeviceLifetime();

        lifetime.Dispose();
        lifetime.Dispose();

        Assert.False(lifetime.TryExecute(static () => { }));
    }
}
