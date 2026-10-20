using MphRead.Mods.Network;
using MphRead.Mods.Movement;

namespace MphRead.Mods.CustomWeapons;

/// <summary>Shared admission policy when the Riftline M7 loadout is requested.
/// Native-only matches do not need this additional policy.</summary>
public static class CustomWeaponMatchPolicy
{
    public static bool TryAdmit(GameMode mode,MatchModifierFlags modifiers,bool online,int bots,out string reason)
    {
        reason="";
        if(bots<0)reason="Bot count cannot be negative.";
        else if(!MovementModelRules.ValidRules(modifiers))reason="Riftline M7 match modifiers are unsupported or contain conflicting movement models.";
        else if(mode!=GameMode.Battle)reason="Riftline M7 requires Battle (free-for-all).";
        else if((modifiers&MatchModifierFlags.InstaGib)!=0)reason="Riftline M7 cannot be combined with Insta-Gib.";
        else if((modifiers&MatchModifierFlags.OneInTheChamber)!=0)reason="Riftline M7 cannot be combined with One in the Chamber.";
        else if((modifiers&MatchModifierFlags.NoImperialist)!=0)reason="Riftline M7 cannot be combined with No Imp.";
        else if((modifiers&MatchModifierFlags.BalancedMode)!=0)reason="Riftline M7 cannot be combined with Balanced Mode.";
        else if((modifiers&MatchModifierFlags.EnhancedHunters)!=0)reason="Riftline M7 cannot be combined with Enhanced Hunters.";
        else if(online&&bots>0)reason="Online Riftline M7 matches require human players without bots.";
        return reason.Length==0;
    }

    // Read configuration only. Runtime eligibility retains its own scene,
    // stream, replay and lifecycle checks outside this shared match policy.
    internal static bool TryAdmit(Scene scene,out string reason)
    {
        var state=scene.GameState;
        var modifiers=(state.InstaGib?MatchModifierFlags.InstaGib:0)
            |(state.OneInTheChamber?MatchModifierFlags.OneInTheChamber:0)
            |(state.NoImperialist?MatchModifierFlags.NoImperialist:0)
            |(state.BalancedMode?MatchModifierFlags.BalancedMode:0)
            |(state.EnhancedHunters?MatchModifierFlags.EnhancedHunters:0);
        int bots=0;
        if(NetSession.Active)
        {
            // BuildPlayers initially clears actor AI. The occupied canonical
            // roster already knows which slots will be authority-owned bots.
            for(int slot=0;slot<NetSession.SlotIsBot.Length;slot++)
                if(NetSession.SlotOccupied[slot]&&NetSession.SlotIsBot[slot])bots++;
        }
        else foreach(var player in scene.Players.Items)if(player.IsBot)bots++;
        return TryAdmit(state.Mode,modifiers,NetSession.Active,bots,out reason);
    }
}
