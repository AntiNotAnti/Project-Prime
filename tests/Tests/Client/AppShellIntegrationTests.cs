using System;
using System.Collections.Generic;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.Network;
using MphRead.Mods.UI.Adapters;
using MphRead.Mods.UI.AppShell;
using MphRead.Mods.UI.Navigation;
using MphRead.Mods.UI.State;
using Xunit;

namespace MphRead.Tests.Client;

public sealed class AppShellIntegrationTests
{
    [Fact]
    public void DesktopWindowUsesPersistentAppShellAsItsRoot()
    {
        using var runtime = new ClientUiRuntime(new MenuSettings(), Array.Empty<string>(),
            isAndroid: false, services: new UiScreenServices());

        AppShellView root = HomeWindow.RootFor(runtime);

        Assert.Same(runtime.Shell, root);
        Assert.Same(ClientSessionCoordinator.Shared, runtime.Coordinator);
    }

    [Fact]
    public void OneShellAndCoordinatorSurviveTwoMatchLifecycles()
    {
        using var runtime = new ClientUiRuntime(new MenuSettings(), Array.Empty<string>(),
            isAndroid: false, services: new UiScreenServices());
        AppShellView shell = runtime.Shell;
        ClientSessionCoordinator coordinator = runtime.Coordinator;

        runtime.MatchStarted();
        runtime.MatchCompleted(replay: true);
        runtime.MatchStarted();
        runtime.MatchCompleted(replay: true);

        Assert.Same(shell, runtime.Shell);
        Assert.Same(coordinator, runtime.Coordinator);
        Assert.Same(ClientSessionCoordinator.Shared, coordinator);
        Assert.Equal(UiRoute.Replays, runtime.State.Router.CurrentRoute);
        Assert.False(runtime.MatchIsActive);
    }

    [Fact]
    public void LobbyAdapterCarriesAuthoritativeLoadingAndGraceFlagsIntoLabels()
    {
        var member = new LobbySnapshotMember(2, 77, "Loading player", Hunter.Samus, 1,
            LobbyWireIdentityKind.Registered,
            LobbyMemberFlags.Loading | LobbyMemberFlags.DisconnectedGrace,
            PingMs: 86);

        UiLobbyMember mapped = CoordinatorLobbyAdapter.MapMember(member, localConnection: 77);

        Assert.True(mapped.Loading);
        Assert.True(mapped.DisconnectedGrace);
        Assert.True(mapped.Local);
        Assert.Equal("Loading · Disconnected grace · Rating eligible", mapped.StatusLabel);
    }
}
