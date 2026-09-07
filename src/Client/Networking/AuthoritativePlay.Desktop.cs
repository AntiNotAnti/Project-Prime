using MphRead.Entities;
namespace MphRead.Mods.Network
{
 public sealed partial class AuthoritativePlay
 {
        public static void Run(string host, int port, string name, Hunter hunter, int recolor)
        {
            hunter = Launcher.Hunters.Resolve(hunter);
            using var play = new AuthoritativePlay(host, port, name, hunter);
            play.Join();
            using var window = new RenderWindow();
            play.BuildPlayers(window.Scene, hunter, recolor);
            window.AddRoom(play.Client.Accepted.Room, play.Client.Accepted.Mode,
                playerCount: NetConfig.RoomPlayerCount);
            window.Run();
        }
 }
}
