#if !ANDROID && !MPHREAD_SERVER
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using MphRead.Mods.Network;

namespace MphRead.Mods.CustomWeapons.Packages;

// Explicit desktop-only entry point. It enables availability, never ownership.
internal static class WeaponPackageDeveloperLaunch
{
    internal static int Run(string[] args)
    {
        if (args.Length is not (4 or 6) || !args[0].Equals("-weaponplay", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(args[1]) || !WeaponIdentity.IsDigest(args[2])
            || string.IsNullOrWhiteSpace(args[3])
            || args.Length == 6 && (!args[4].Equals("-seconds", StringComparison.OrdinalIgnoreCase)
                || !int.TryParse(args[5], NumberStyles.None, CultureInfo.InvariantCulture, out int seconds)
                || seconds is < 1 or > 600))
        {
            Console.Error.WriteLine("Usage: -weaponplay <store> <exact-lowercase-package-sha256> <stock-room> [-seconds 1..600]");
            return 2;
        }
        try
        {
            string room = args[3];
            if (NetSession.Active || DemoPlayback.IsActive || Headless.Active || ReplayCapture.LiveRecordingActive
                || !StockRiftlinePickups.Supports(room)
                || Metadata.GetRoomByName(room).Item1 is not { Multiplayer: true }
                || MapGen.CustomRooms.Definitions.Any(map => string.Equals(map.Name, room, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Package play requires a fresh offline scene and an authored stock multiplayer room.");
            var store = new WeaponPackageStore(args[1]);
            // Refuse missing/corrupt/uninstalled revisions before creating a window.
            // The Scene acquires its own lease when it activates after native load.
            using (store.AcquireInstalled(args[2])) { }
            Menu.SaveSlot = 0;
            RenderWindow.LogCreatingWindow();
            using var window = RenderWindow.Create();
            window.Scene.AddPlayer(Hunter.Samus);
            window.AddRoom(room, GameMode.Battle, playerCount: 1);
            window.Load += () =>
            {
                window.Scene.EnableOfflinePackageAvailability(store, args[2], optIn: true);
                window.Scene.GameState.MatchTime = -1;
                Console.WriteLine("Package play: offline Samus Battle. Collect the world weapon pickup to acquire it. Replay recording is unavailable.");
            };
            if (args.Length == 6)
            {
                ulong ticks = (ulong)(int.Parse(args[5], CultureInfo.InvariantCulture) * Render.FrameTiming.SimulationHz);
                window.UpdateFrame += _ => { if (window.Scene.FrameCount >= ticks) window.Close(); };
            }
            window.Run();
            return 0;
        }
        catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException
            or InvalidOperationException or NotSupportedException or System.Security.SecurityException)
        {
            string detail = new(error.Message.Take(512).Select(c => c is >= ' ' and <= '~' ? c : '?').ToArray());
            Console.Error.WriteLine("Package play refused: " + detail);
            return 2;
        }
    }
}
#endif
