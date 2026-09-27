using MphRead.Mods.Render.Hud;
namespace MphRead.Entities;

public partial class PlayerEntity
{
    // Reads the current player's authorized presentation values. This adapter never
    // requests additional information and never writes game or network state.
    private bool HudElementVisible(int index)
    {
        var runtime = HudProfiles.Runtime;
        if (index < 0 || runtime.Mode != HudMode.Custom) return true;
        var context = Mods.Network.DemoPlayback.IsActive ? HudContext.Replay
            : Mods.SpectatorMode.FreeCamera ? HudContext.SpectatorFree
            : Mods.SpectatorMode.IsSpectating ? HudContext.SpectatorPov : HudContext.Playing;
        if ((runtime[index].Contexts & context) == 0) return false;
        var condition = runtime[index].Visibility;
        return condition switch
        {
            HudVisibility.Combat => _timeSinceDamage < 60,

            HudVisibility.Multiplayer => _scene.GameState.Multiplayer,
            HudVisibility.Spectator => Mods.SpectatorMode.IsSpectating,
            HudVisibility.Damaged => ModHudHealthVisible && ProHealthFraction() < 1,
            HudVisibility.AmmoNotFull => EquipInfo.Weapon.AmmoCost > 0 && ProAmmoFraction() < 1,
            HudVisibility.Objective => _scene.GameState.Mode is GameMode.Bounty or GameMode.BountyTeams or GameMode.Capture or GameMode.Defender or GameMode.DefenderTeams or GameMode.Nodes or GameMode.NodesTeams,
            _ => true
        };
    }
    private Scene.HudLayoutScope UseHudLayout(int index, float x, float y)
        => _scene.PushHudElement(index,x,y,HudElementVisible(index));
}
