using MphRead.Entities;

namespace MphRead.Mods.Network
{
    /// <summary>
    /// The scoreboard's half of "whoever is arriving is not whoever left".
    ///
    /// Every other per-slot record the net code keeps is cleared when a slot
    /// changes hands -- the reported positions and frame numbers, the spawn
    /// barriers, the damage sequence, the wire's intent validity (see
    /// NetPlayerBridge, NetDamage and NetSession's own ForgetSlot). The score
    /// was the one that was not, and it is the one a player can see: kill
    /// somebody, leave the match, come back into the same slot, and the kill
    /// was still on the board with your name against it.
    ///
    /// It has to be cleared on every machine and not only on the one that
    /// left, because the scoreboard belongs to the authority: it publishes
    /// Points, Kills and Deaths for every slot in each snapshot and every
    /// other client adopts them (NetPlayerBridge.ApplyState). A client that
    /// cleared its own copy would have it handed straight back.
    ///
    /// Team totals are not touched. They are recomputed from these every
    /// update (MatchFlow.UpdateStandings sums Points into TeamPoints), so
    /// clearing the slot is what clears the team, and clearing the team
    /// directly would be wrong in a team mode anyway -- the points were the
    /// team's, and the team is still playing.
    /// </summary>
    public static class NetScoreboard
    {
        /// <summary>Everything the match attributes to one slot, for a slot about to change hands.</summary>
        public static void ForgetSlot(Scene scene, int slot)
        {
            if (slot < 0 || slot >= PlayerEntity.SlotCapacity)
            {
                return;
            }
            scene.Match.Players[slot].Points = 0;
            scene.Match.Players[slot].Kills = 0;
            scene.Match.Players[slot].Deaths = 0;
            scene.Match.Players[slot].Assists = 0;
            scene.Match.Players[slot].LongestKillStreak = 0;
            scene.Match.Players[slot].DamageDealt = 0;
            scene.Match.Players[slot].BipedKills = 0;
            scene.Match.Players[slot].AltFormKills = 0;
            scene.Match.Players[slot].Time = 0;
            scene.Match.Players[slot].Suicides = 0;
            scene.Match.Players[slot].FriendlyKills = 0;
            scene.Match.Players[slot].HeadshotKills = 0;
            scene.Match.Players[slot].DamageCount = 0;
            scene.Match.Players[slot].AltDamageCount = 0;
            scene.Match.Players[slot].BeamDamageDealt = 0;
            scene.Match.Players[slot].BeamDamageMax = 0;
            scene.Match.Players[slot].OctolithScores = 0;
            scene.Match.Players[slot].OctolithDrops = 0;
            scene.Match.Players[slot].OctolithStops = 0;
            scene.Match.Players[slot].NodesCaptured = 0;
            scene.Match.Players[slot].NodesLost = 0;
            scene.Match.Players[slot].KillsAsPrime = 0;
            scene.Match.Players[slot].PrimesKilled = 0;
            for (int beam = 0; beam < 9; beam++)
            {
                scene.Match.Players[slot].SetBeamKills(beam, 0);
            }
        }
    }
}
