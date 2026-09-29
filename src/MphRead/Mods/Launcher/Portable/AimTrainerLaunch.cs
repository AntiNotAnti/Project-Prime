using MphRead.Mods.Training;
namespace MphRead.Mods.Launcher;
public static class AimTrainerLaunch
{
    public const string Room = "PRIME AIM LAB";
    public static LaunchPlan Create(AimTrainerDefinition definition, Hunter hunter, int suit)
    {
        definition = definition.Sanitize();
        LauncherPrefs.Training = definition;
        LauncherPrefs.LastHunter = hunter;
        LauncherPrefs.LastColor = System.Math.Clamp(suit, 0, 3);
        LauncherPrefs.Save();
        return new LaunchPlan { Kind = LaunchKind.AimTrainer, Training = definition, RoomKey = Room,
            Mode = GameMode.Battle, Hunter = hunter, Bots = definition.TargetCount, BotLevel = 0,
            MatchRules = new Network.MatchDefinition { Mode = GameMode.Battle }, PlayerName = LauncherPrefs.PlayerName };
    }
}
