using System;
using MphRead.Mods.Launcher.Gui;
using Xunit;

namespace MphRead.Tests.Client;

public sealed class PrimeSeatOfferModalStateTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 10, 12, 0, 0,
        TimeSpan.Zero);

    [Fact]
    public void RevisionOnlyUpdatesDoNotReopenOrReplaceActiveOffer()
    {
        var state = new PrimeSeatOfferModalState();
        var key = new PrimeSeatOfferKey(Guid.NewGuid(), Guid.NewGuid());

        Assert.Equal(PrimeSeatOfferTransition.Open,
            state.Observe(Offer(key, revision: 4), Now));
        Assert.Equal(PrimeSeatOfferTransition.None,
            state.Observe(Offer(key, revision: 9), Now.AddSeconds(1)));
        Assert.Equal(key, state.Active);
    }

    [Fact]
    public void SupersedingOfferReusesSingleModalOwnerWithNewIdentity()
    {
        var state = new PrimeSeatOfferModalState();
        var first = new PrimeSeatOfferKey(Guid.NewGuid(), Guid.NewGuid());
        var second = new PrimeSeatOfferKey(first.LobbyId, Guid.NewGuid());

        Assert.Equal(PrimeSeatOfferTransition.Open,
            state.Observe(Offer(first), Now));
        Assert.Equal(PrimeSeatOfferTransition.Open,
            state.Observe(Offer(second), Now));
        Assert.Equal(second, state.Active);
    }

    [Fact]
    public void CompletedOfferStaysSuppressedUntilItsIdentityChanges()
    {
        var state = new PrimeSeatOfferModalState();
        var first = new PrimeSeatOfferKey(Guid.NewGuid(), Guid.NewGuid());
        var second = new PrimeSeatOfferKey(first.LobbyId, Guid.NewGuid());
        state.Observe(Offer(first), Now);

        Assert.True(state.Complete(first));
        Assert.Equal(PrimeSeatOfferTransition.None,
            state.Observe(Offer(first, revision: 2), Now.AddSeconds(1)));
        Assert.Equal(PrimeSeatOfferTransition.Open,
            state.Observe(Offer(second), Now.AddSeconds(1)));
    }

    [Fact]
    public void RejectedCommandCanReleaseOnlyItsSameAuthoritativeOffer()
    {
        var state = new PrimeSeatOfferModalState();
        var key = new PrimeSeatOfferKey(Guid.NewGuid(), Guid.NewGuid());
        var other = new PrimeSeatOfferKey(key.LobbyId, Guid.NewGuid());
        state.Observe(Offer(key), Now);
        state.Complete(key);

        Assert.False(state.Release(other));
        Assert.Equal(PrimeSeatOfferTransition.None,
            state.Observe(Offer(key), Now.AddSeconds(1)));
        Assert.True(state.Release(key));
        Assert.Equal(PrimeSeatOfferTransition.Open,
            state.Observe(Offer(key), Now.AddSeconds(1)));
    }

    [Fact]
    public void ExpiryIsAnnouncedOnceAndCannotReopenFromStaleSnapshots()
    {
        var state = new PrimeSeatOfferModalState();
        var key = new PrimeSeatOfferKey(Guid.NewGuid(), Guid.NewGuid());

        Assert.Equal(PrimeSeatOfferTransition.Expired,
            state.Observe(Offer(key, expiresAt: Now), Now));
        Assert.Equal(PrimeSeatOfferTransition.None,
            state.Observe(Offer(key, expiresAt: Now), Now.AddSeconds(1)));
        Assert.Null(state.Active);
    }

    [Fact]
    public void DisconnectAndTeardownCloseWithoutConsumingLiveOffer()
    {
        var state = new PrimeSeatOfferModalState();
        var key = new PrimeSeatOfferKey(Guid.NewGuid(), Guid.NewGuid());
        state.Observe(Offer(key), Now);

        Assert.Equal(PrimeSeatOfferTransition.Close,
            state.Observe(null, Now.AddSeconds(1)));
        Assert.Equal(PrimeSeatOfferTransition.Open,
            state.Observe(Offer(key), Now.AddSeconds(2)));
        Assert.True(state.Suspend());
        Assert.Equal(PrimeSeatOfferTransition.Open,
            state.Observe(Offer(key), Now.AddSeconds(3)));
    }

    private static PrimeSeatOfferObservation Offer(PrimeSeatOfferKey key,
        long revision = 1, DateTimeOffset? expiresAt = null)
        => new(key, revision, expiresAt ?? Now.AddSeconds(12));
}
