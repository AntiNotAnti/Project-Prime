using System.Reflection;
using Avalonia.Controls;
using Avalonia.Threading;
using MphRead;
using MphRead.Entities;
using MphRead.Mods;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.Network;

internal static class LobbyReturnAcceptance
{
    internal static int Run()
    {
        const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        bool native = Environment.GetEnvironmentVariable("PROJECT_PRIME_RMLUI_GL_SMOKE") != "0";
        int checks = 0;
        void Check(bool pass, string label)
        {
            if (!pass) throw new InvalidOperationException(label);
            checks++;
            Console.WriteLine("LOBBY RETURN PASS " + label);
        }
        var scene = new Scene(default, null!, null!, _ => { }, () => { }, initializeRuntime: false);
        GameState.Current = scene.GameState;
        PlayerEntity.LegacyRegistry = scene.Players;
        bool still = Deck.Still;
        Deck.Still = true;
        try
        {
            Dispatcher.UIThread.Invoke(() =>
            {
                // Real detached StartScreen, connection handoff and close handlers.
                // Only session facts/startup completion are staged; no transport,
                // account, GL context, settings/save write or live server is used.
                foreach (string ending in new[] { "leave", "server-close", "connection-loss" })
                {
                    using var home = new StartScreen(new MenuSettings { RoomKey = "MP1 SANCTORUS" }, new[] { "MP1 SANCTORUS" });
                    if (typeof(StartScreen).GetField("_startup", Hidden)!.GetValue(home) is Control startup)
                    {
                        ((Panel)typeof(StartScreen).GetField("_layers", Hidden)!.GetValue(home)!).Children.Remove(startup);
                        (startup as IDisposable)?.Dispose();
                        typeof(StartScreen).GetField("_startup", Hidden)!.SetValue(home, null);
                    }
                    typeof(StartScreen).GetField("_nativeStartupPending", Hidden)!.SetValue(home, false);
                    typeof(NetSession).GetProperty(nameof(NetSession.Role))!.SetValue(null, NetRole.Client);
                    typeof(NetSession).GetProperty(nameof(NetSession.ServerSession))!.SetValue(null,
                        new SessionStatePacket { AuthorityEpoch = 7, MatchId = 1, Revision = 1, OwnerSlot = 0,
                            Phase = SessionPhase.Lobby, Policy = ServerSessionPolicy.Lobby, MaxPlayers = 8,
                            Match = new MatchDefinition { RoomKey = "MP1 SANCTORUS", Mode = GameMode.Battle } });
                    typeof(StartScreen).GetMethod("ConnectedOrFinished", Hidden)!.Invoke(home,
                        new object[] { new LaunchPlan { RoomKey = "MP1 SANCTORUS" } });
                    var lobby = (LobbyScreen)typeof(StartScreen).GetField("_lobby", Hidden)!.GetValue(home)!;
                    Check(home.Prime.Router.Current == PrimeRoute.Lobby && NetSession.Active,
                        ending + ": real connection handoff retains the lobby");
                    if (ending == "leave") Check(home.RmlUiLeaveLobby(), "native Leave uses existing teardown");
                    else if (ending == "server-close") lobby.Leave("Lobby closed.");
                    else { NetSession.Stop(); lobby.SessionTick(foreground: false); }
                    Check(home.Prime.Router.Current == (native ? PrimeRoute.News : PrimeRoute.Play),
                        ending + ": returns to " + (native ? "native Play/Home shell route" : "legacy Play when native UI is disabled"));
                    Check(!NetSession.Active && typeof(StartScreen).GetField("_lobby", Hidden)!.GetValue(home) == null
                        && home.Prime.Workspaces.TryGet(PrimeRoute.Lobby) == null
                        && !home.Prime.Overlays.IsOpen && home.Prime.Router.Previous != PrimeRoute.Lobby,
                        ending + ": session, workspace, overlay and lobby history retire");
                    var coordinator = (LobbySessionCoordinator)typeof(StartScreen).GetField("_session", Hidden)!.GetValue(home)!;
                    Check(coordinator.Screen == null && !home.RmlUiLeaveLobby() && home.RmlUiCanPresent,
                        ending + ": no stale pump or duplicate leave; front is ready for presentation");
                }
            });
            Console.WriteLine($"LOBBY RETURN {checks} checks passed; native={native}.");
            return 0;
        }
        finally
        {
            NetSession.Stop();
            Deck.Still = still;
            try { scene.DoCleanup(); } finally { scene.UnloadGl(); }
        }
    }
}
