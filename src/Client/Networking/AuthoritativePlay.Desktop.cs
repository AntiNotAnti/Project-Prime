using MphRead.Entities;
using ProjectPrime.Server.Shared;
using MapGen = MphRead.Mods.MapGen;
namespace MphRead.Mods.Network
{
 internal static class AuthoritativePlayDesktop
 {
        internal static bool PumpSceneCompletion(AuthoritativePlay play, Scene scene, SdlGameHost host)
        {
            if (!play.ObserveCompletion()) return false;
            host.SetCursorCaptured(false);
            if (play.State == AuthoritativePlay.TerminalState.Transitioning)
            {
                // A transition has no results payload. Dispose only the
                // per-match scene and leave the persistent SDL host alive for
                // the replacement handoff.
                host.StopScene();
                Launcher.Gui.GuiLauncher.Pump();
                return true;
            }
            bool drainFinished = play.DrainCompletion(scene);
            // Draining terminal UDP events may have armed the final replay on
            // this same pump. Let the normal frame loop advance and render it;
            // close only after the controller releases terminal presentation.
            if (ShouldStopCompletedScene(drainFinished,
                    host.BlocksSceneCompletion)) host.StopScene();
            Launcher.Gui.GuiLauncher.Pump();
            if (host.BlocksSceneCompletion) return false;
            System.Threading.Thread.Sleep(1);
            return true;
        }

        internal static bool ShouldStopCompletedScene(bool drainFinished,
            bool blocksSceneCompletion)
            => drainFinished && !blocksSceneCompletion;

        public static void Run(string host, int port, string name, Hunter hunter, int recolor)
        {
            hunter = Launcher.Hunters.Resolve(hunter);
            using var runtime = new ClientOnlineRuntime();
            using var play = new AuthoritativePlay(host, port, name, hunter);
            play.Join();
            MatchClientContext match = runtime.AdoptMatch(play, System.Guid.NewGuid())!;
            MapGen.RoomContentPreparationResult preparation =
                MapGen.MapPreparation.PrepareRoomAsync(
                    new MapGen.RoomContentRequest(play.Client.Accepted.Room, null,
                        MapGen.GameplayContentIdentity.Current(
                            BuildIdentity.Display, NetHeader.Version),
                        MapGen.RoomContentPurpose.Match),
                    System.Threading.CancellationToken.None)
                .GetAwaiter().GetResult();
            MapGen.MapPreparation.RequirePreparedRoom(preparation);
            var scene = new Scene(features: ClientMatchFeatures.Capture());
            using var sdlHost = new SdlGameHost();
            try
            {
                sdlHost.RunScene(scene, presentation =>
                {
                    play.BuildPlayers(scene, hunter, recolor);
                    presentation.AddRoom(play.Client.Accepted.Room, play.Client.Accepted.Mode,
                        playerCount: play.Client.Accepted.Rules.EntityLayerPlayerCount);
                }, suspendFrame: () => PumpSceneCompletion(play, scene, sdlHost),
                    sceneServices: new ClientSceneServices(match, runtime.Node));
            }
            finally
            {
                ContentEnvironment.UnmountMap();
            }
        }
 }
}
