using System;
using System.Collections.Immutable;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Accounts;
using MphRead.Mods.Launcher.Gui;
using ProjectPrime.Server.Shared;
using Xunit;

namespace MphRead.Tests;

public sealed class PresenceRefreshTests
{
    [Fact]
    public async Task PresenceLeaseDoesNotOverlapAndDiscardsCompletionAfterRouteExit()
    {
        var handler = new BlockingHandler();
        using var account = new AccountSession(new Uri("https://backend.example/"), handler);
        await using var controller = new PlayController(new PrimeShellState(),
            accountResolver: _ => Task.FromResult<AccountSession?>(account));

        controller.SetPresenceRefreshEnabled(true);
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await controller.RefreshPresenceAsync();
        Assert.Equal(1, handler.RequestCount);

        controller.SetPresenceRefreshEnabled(false);
        handler.Complete(Page());
        await Task.Delay(50);

        Assert.NotEqual(PresenceLoadState.Ready, controller.Presence.State);
        Assert.Equal(1, handler.RequestCount);
    }

    [Theory]
    [InlineData(PresenceLoadState.Loading, 0, "Checking…")]
    [InlineData(PresenceLoadState.Ready, 1, "● 1 ONLINE")]
    [InlineData(PresenceLoadState.Ready, 18, "● 18 ONLINE")]
    [InlineData(PresenceLoadState.Failed, 0, "Status unavailable")]
    [InlineData(PresenceLoadState.Failed, 18, "● 18 ONLINE")]
    public void BadgeCopyUsesRequestedPopulationStates(PresenceLoadState state,
        int total, string expected)
    {
        var presentation = new PresencePresentationState(state, total, 0,
            ImmutableArray<PublicPresenceEntry>.Empty, 0, default);

        Assert.Equal(expected, presentation.BadgeText);
    }

    [Fact]
    public void FreshnessOnlyChangeDoesNotRepublishPresencePresentation()
    {
        var entries = ImmutableArray.Create(new PublicPresenceEntry("Hunter",
            PlayerPresenceActivity.Online, "us"));
        var first = new PresencePresentationState(PresenceLoadState.Ready,
            1, 1, entries, 4, DateTimeOffset.UtcNow);
        var refreshed = first with
        {
            Players = ImmutableArray.Create(new PublicPresenceEntry("Hunter",
                PlayerPresenceActivity.Online, "us")),
            GeneratedAt = first.GeneratedAt.AddSeconds(12)
        };

        Assert.True(PlayController.SamePresencePresentation(first, refreshed));
        Assert.False(PlayController.SamePresencePresentation(first,
            refreshed with { TotalOnline = 2 }));
    }

    private static PresenceDirectoryPage Page()
        => new(7, 1, 1, 0, 1,
            ImmutableArray.Create(new PublicPresenceEntry("Hunter",
                PlayerPresenceActivity.Online, "us")),
            DateTimeOffset.UtcNow);

    private sealed class BlockingHandler : HttpMessageHandler
    {
        private readonly TaskCompletionSource<HttpResponseMessage> _response =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int RequestCount;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("/v1/presence", request.RequestUri!.AbsolutePath);
            Assert.Null(request.Headers.Authorization);
            Interlocked.Increment(ref RequestCount);
            Started.TrySetResult();
            // Deliberately ignore cancellation to prove a stale completion
            // cannot republish after the route lease is revoked.
            return _response.Task;
        }

        public void Complete(PresenceDirectoryPage page)
            => _response.TrySetResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(page,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                    Encoding.UTF8, "application/json")
            });
    }
}
