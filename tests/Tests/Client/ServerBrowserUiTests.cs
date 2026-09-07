using Avalonia.Media;
using MphRead.Mods.Network;
using MphRead.Mods.UI.Components;
using MphRead.Mods.UI.State;
using Xunit;

namespace MphRead.Tests.Client;

public sealed class ServerBrowserUiTests
{
    [Fact]
    public void MapCardKeepsPlaceholderUntilAThumbnailIsAvailable()
    {
        var card = new MapCard();

        Assert.False(card.HasPreview);
        card.ShowPreview(new DrawingImage());
        Assert.True(card.HasPreview);
        card.ShowPlaceholder();
        Assert.False(card.HasPreview);
    }

    [Theory]
    [InlineData(ServerJoinDisposition.JoinNow, "Join now", true, false)]
    [InlineData(ServerJoinDisposition.JoinLobby, "Join lobby", true, false)]
    [InlineData(ServerJoinDisposition.Spectate, "Spectate", false, false)]
    [InlineData(ServerJoinDisposition.WaitForNextMatch, "Wait for next match", false, false)]
    [InlineData(ServerJoinDisposition.Full, "Full", false, true)]
    [InlineData(ServerJoinDisposition.Closed, "Closed", false, false)]
    public void EntryUsesAuthoritativeJoinDisposition(ServerJoinDisposition disposition,
        string label, bool canJoin, bool full)
    {
        UiServerEntry entry = Server() with
        {
            HasSessionState = true,
            JoinDisposition = disposition
        };

        Assert.Equal(label, entry.JoinLabel);
        Assert.Equal(canJoin, entry.CanJoin);
        Assert.Equal(full, entry.Full);
    }

    [Fact]
    public void HideFullUsesAuthoritativeDispositionInsteadOfStalePopulation()
    {
        UiServerEntry entry = Server() with
        {
            Players = 1,
            MaxPlayers = 8,
            HasSessionState = true,
            JoinDisposition = ServerJoinDisposition.Full
        };

        Assert.Empty(UiServerSelection.Apply([entry], new UiServerFilter("", null,
            HideFull: true, HideIncompatible: false, MaxPing: 0, UiServerSort.Ping,
            UiServerGroup.All)));
    }

    private static UiServerEntry Server() => new("id", "Server", "127.0.0.1:27888",
        "Sanctorus", "Battle", 2, 8, 0, 0, 20, "Lobby", null, "Classic",
        Verified: true, Ranked: false, Compatible: true, Favorite: false, Recent: false,
        FriendlyFire: false, Radar: true, SpawnPolicy: "Default", LateJoin: true,
        Private: false);
}
