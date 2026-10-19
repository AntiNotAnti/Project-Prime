#if !ANDROID && !MPHREAD_SERVER
using System;
using MphRead.Mods.Network;

namespace MphRead.Mods.Movement;

internal static class MomentumDeveloperLaunch
{
    internal static int Run(string room, Hunter hunter, int? ticks)
    {
        if (NetSession.Active || DemoPlayback.IsActive || Headless.Active
            || hunter is < Hunter.Samus or > Hunter.Weavel
            || Metadata.GetRoomByName(room).Item1 is not { Multiplayer: true })
        {
            Console.Error.WriteLine("Momentum developer practice requires an offline multiplayer room and a playable Hunter.");
            return 2;
        }
        Menu.SaveSlot = 0;
        RenderWindow.LogCreatingWindow();
        using var window = RenderWindow.Create();
        window.Scene.AddPlayer(hunter);
        window.AddRoom(room, GameMode.Battle, playerCount: 1);
        if (!window.Scene.TryEnableDeveloperMomentum())
        {
            Console.Error.WriteLine("The scene refused Momentum developer practice.");
            return 2;
        }
        window.Scene.GameState.MatchTime = -1;
        Console.WriteLine($"Momentum developer profile v{MomentumProfileV1.MomentumProfileVersion}: offline biped experiment; Classic remains the default.");
        if (ticks is { } limit)
            window.UpdateFrame += _ => { if (window.Scene.FrameCount >= (ulong)limit) window.Close(); };
        window.Run();
        return 0;
    }
}

#endif
