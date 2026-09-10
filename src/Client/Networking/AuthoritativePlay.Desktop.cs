using MphRead.Entities;
namespace MphRead.Mods.Network
{
 public sealed partial class AuthoritativePlay
 {
        internal bool PumpSceneCompletion(Scene scene, SdlGameHost host)
        {
            if (!ObserveCompletion()) return false;
            host.SetCursorCaptured(false);
            if (DrainCompletion(scene)) host.StopScene();
            Launcher.Gui.GuiLauncher.Pump();
            System.Threading.Thread.Sleep(1);
            return true;
        }

        public static void Run(string host, int port, string name, Hunter hunter, int recolor)
        {
            hunter = Launcher.Hunters.Resolve(hunter);
            using var play = new AuthoritativePlay(host, port, name, hunter);
            play.Join();
            var scene = new Scene(features: ClientMatchFeatures.Capture());
            using var sdlHost = new SdlGameHost();
            sdlHost.RunScene(scene, presentation =>
            {
                play.BuildPlayers(scene, hunter, recolor);
                presentation.AddRoom(play.Client.Accepted.Room, play.Client.Accepted.Mode,
                    playerCount: NetConfig.RoomPlayerCount);
            }, suspendFrame: () => play.PumpSceneCompletion(scene, sdlHost));
        }
 }
}
