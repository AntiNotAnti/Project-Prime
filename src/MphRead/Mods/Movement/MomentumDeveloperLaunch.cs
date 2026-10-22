#if !ANDROID && !MPHREAD_SERVER
using System;
using MphRead.Mods.Network;

namespace MphRead.Mods.Movement;

internal static class MomentumDeveloperLaunch
{
    internal static int Run(string room, Hunter hunter, int? ticks, MomentumPracticeTuning? practiceTuning = null)
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
        if (!window.Scene.TryEnableDeveloperMomentum(practiceTuning: practiceTuning))
        {
            Console.Error.WriteLine("The scene refused Momentum developer practice.");
            return 2;
        }
        window.Scene.GameState.MatchTime = -1;
        if (practiceTuning == null)
            Console.WriteLine($"Momentum developer profile v{MomentumProfileV1.MomentumProfileVersion}: offline biped experiment; Classic remains the default.");
        else
        {
            Console.WriteLine("Quake III strafe-jump tuning draft: offline only; Classic remains the default. Online and recorded profile v1 are unchanged.");
            Console.WriteLine($"Ground speed {practiceTuning.GroundSpeed}, acceleration {practiceTuning.Tuning.GroundAcceleration}, friction {practiceTuning.Tuning.GroundFriction}; air acceleration {practiceTuning.Tuning.AirAcceleration}, wish limit {practiceTuning.Tuning.AirWishSpeedCap}; no total speed cap.");
        }
        if (ticks is { } limit)
            window.UpdateFrame += _ => { if (window.Scene.FrameCount >= (ulong)limit) window.Close(); };
        window.Run();
        return 0;
    }
}

#endif
