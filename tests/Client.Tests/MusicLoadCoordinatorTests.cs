using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Formats.Sound;
using MphRead.Sound;
using Xunit;

namespace MphRead.Tests.Client;

public sealed class MusicLoadCoordinatorTests
{
    [Fact]
    public async Task LatestRequestWinsWithOneActiveDecodeAndOnePendingSlot()
    {
        var firstStarted = NewSignal();
        var releaseFirst = NewSignal();
        var published = new ConcurrentQueue<SeqId>();
        var resources = new ConcurrentBag<TestResource>();
        int active = 0;
        int maximumActive = 0;

        using var coordinator = new MusicLoadCoordinator(
            async (request, _) =>
            {
                int current = Interlocked.Increment(ref active);
                UpdateMaximum(ref maximumActive, current);
                try
                {
                    if (request.Generation == 1)
                    {
                        firstStarted.SetResult(true);
                        await releaseFirst.Task;
                    }
                    var resource = new TestResource(request.Sequence);
                    resources.Add(resource);
                    return resource;
                }
                finally
                {
                    Interlocked.Decrement(ref active);
                }
            },
            (request, result) =>
            {
                published.Enqueue(request.Sequence);
                return true;
            });

        MusicLoadCoordinator.RequestHandle first =
            coordinator.Enqueue((SeqId)1, UInt16.MaxValue);
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        MusicLoadCoordinator.RequestHandle second =
            coordinator.Enqueue((SeqId)2, UInt16.MaxValue);
        MusicLoadCoordinator.RequestHandle third =
            coordinator.Enqueue((SeqId)3, UInt16.MaxValue);
        MusicLoadCoordinator.RequestHandle latest =
            coordinator.Enqueue((SeqId)4, UInt16.MaxValue);

        Assert.Equal(MusicLoadCoordinator.CompletionStatus.Superseded,
            (await second.Completion.WaitAsync(TimeSpan.FromSeconds(2))).Status);
        Assert.Equal(MusicLoadCoordinator.CompletionStatus.Superseded,
            (await third.Completion.WaitAsync(TimeSpan.FromSeconds(2))).Status);

        releaseFirst.SetResult(true);
        Assert.Equal(MusicLoadCoordinator.CompletionStatus.Superseded,
            (await first.Completion.WaitAsync(TimeSpan.FromSeconds(2))).Status);
        Assert.Equal(MusicLoadCoordinator.CompletionStatus.Published,
            (await latest.Completion.WaitAsync(TimeSpan.FromSeconds(2))).Status);

        MusicLoadCoordinator.Diagnostics snapshot = coordinator.Snapshot;
        Assert.Equal(1, maximumActive);
        Assert.Equal(1, snapshot.MaxDepth);
        Assert.Equal(0, snapshot.CurrentDepth);
        Assert.Contains((SeqId)4, published);
        Assert.DoesNotContain((SeqId)1, published);
        Assert.DoesNotContain((SeqId)2, published);
        Assert.DoesNotContain((SeqId)3, published);
        Assert.True(snapshot.Superseded >= 3);
        Assert.True(snapshot.Cancellations >= 3);

        await coordinator.DrainAsync().WaitAsync(TimeSpan.FromSeconds(2));
        foreach (TestResource resource in resources) resource.Dispose();
        Assert.All(resources, resource => Assert.True(resource.IsDisposed));
    }

    [Fact]
    public async Task StopCancelsActiveLoadWithoutWaitingForDecoder()
    {
        var started = NewSignal();
        var release = NewSignal();
        var published = new ConcurrentQueue<SeqId>();
        var resource = new TestResource((SeqId)11);

        using var coordinator = new MusicLoadCoordinator(
            async (_, _) =>
            {
                started.SetResult(true);
                await release.Task;
                return resource;
            },
            (request, _) =>
            {
                published.Enqueue(request.Sequence);
                return true;
            });

        MusicLoadCoordinator.RequestHandle handle =
            coordinator.Enqueue((SeqId)11, UInt16.MaxValue);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        coordinator.Cancel();
        Assert.False(handle.Completion.IsCompleted);

        release.SetResult(true);
        MusicLoadCoordinator.Completion completion =
            await handle.Completion.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(MusicLoadCoordinator.CompletionStatus.Canceled, completion.Status);
        Assert.Empty(published);
        Assert.True(resource.IsDisposed);
        await coordinator.DrainAsync().WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task ShutdownDrainsConsumerAndRejectsLatePublication()
    {
        var started = NewSignal();
        var release = NewSignal();
        var published = new ConcurrentQueue<SeqId>();
        var resource = new TestResource((SeqId)21);

        using var coordinator = new MusicLoadCoordinator(
            async (_, _) =>
            {
                started.SetResult(true);
                await release.Task;
                return resource;
            },
            (request, _) =>
            {
                published.Enqueue(request.Sequence);
                return true;
            });

        MusicLoadCoordinator.RequestHandle handle =
            coordinator.Enqueue((SeqId)21, UInt16.MaxValue);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Task drain = coordinator.DrainAsync();
        release.SetResult(true);

        await drain.WaitAsync(TimeSpan.FromSeconds(2));
        MusicLoadCoordinator.Completion completion =
            await handle.Completion.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(MusicLoadCoordinator.CompletionStatus.Shutdown, completion.Status);
        Assert.Empty(published);
        Assert.True(resource.IsDisposed);
        Assert.False(coordinator.Snapshot.Active);
        Assert.Equal(0, coordinator.Snapshot.CurrentDepth);
    }

    [Fact]
    public async Task FailedDecodeDoesNotPoisonNextRequest()
    {
        int attempts = 0;
        var published = new ConcurrentQueue<SeqId>();
        var resource = new TestResource((SeqId)32);
        var observedFailure = new TaskCompletionSource<MusicLoadCoordinator.Completion>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        using var coordinator = new MusicLoadCoordinator(
            (request, _) =>
            {
                if (Interlocked.Increment(ref attempts) == 1)
                {
                    return Task.FromException<IDisposable>(
                        new InvalidOperationException("synthetic decode failure"));
                }
                return Task.FromResult<IDisposable>(resource);
            },
            (request, _) =>
            {
                published.Enqueue(request.Sequence);
                return true;
            });

        Action<MusicLoadCoordinator.Completion> observeFailure = completion =>
        {
            if (completion.Status == MusicLoadCoordinator.CompletionStatus.Failed)
            {
                observedFailure.TrySetResult(completion);
            }
        };

        MusicLoadCoordinator.Completion first = await coordinator
            .Enqueue((SeqId)31, UInt16.MaxValue, observer: observeFailure).Completion
            .WaitAsync(TimeSpan.FromSeconds(2));
        MusicLoadCoordinator.Completion second = await coordinator
            .Enqueue((SeqId)32, UInt16.MaxValue, observer: observeFailure).Completion
            .WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(MusicLoadCoordinator.CompletionStatus.Failed, first.Status);
        Assert.NotNull(first.Error);
        Assert.Equal("synthetic decode failure", first.Error!.Message);
        Assert.Same(first, await observedFailure.Task.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal(MusicLoadCoordinator.CompletionStatus.Published, second.Status);
        Assert.Equal(1, coordinator.Snapshot.Failures);
        Assert.Equal(1, coordinator.Snapshot.Published);
        Assert.Equal(new[] { (SeqId)32 }, published.ToArray());

        await coordinator.DrainAsync().WaitAsync(TimeSpan.FromSeconds(2));
        resource.Dispose();
        Assert.True(resource.IsDisposed);
    }

    private static TaskCompletionSource<bool> NewSignal()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static void UpdateMaximum(ref int maximum, int value)
    {
        while (true)
        {
            int current = Volatile.Read(ref maximum);
            if (value <= current || Interlocked.CompareExchange(
                    ref maximum, value, current) == current) return;
        }
    }

    private sealed class TestResource : IDisposable
    {
        internal TestResource(SeqId sequence) => Sequence = sequence;

        internal SeqId Sequence { get; }
        internal bool IsDisposed { get; private set; }

        public void Dispose() => IsDisposed = true;
    }
}
