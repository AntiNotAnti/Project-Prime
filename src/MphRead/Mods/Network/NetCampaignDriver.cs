using System;
using System.Linq;

namespace MphRead.Mods.Network;

public sealed partial class DedicatedServer
{
    // Continuous servers have no lobby owner. The isolated N0 campaign must drive
    // its bot fixture from the authority, using the existing bot lifecycle methods.
    // Both explicit test flags are required; ordinary server behavior is unchanged.
    private static readonly bool CampaignBotEnabled = NetCampaignEvidence.Enabled
        && Environment.GetEnvironmentVariable("PRIME_NET2_SOAK") == "1";
    private double _campaignBotReadyAt = -1;
    private int _campaignBotSlot = -1;
    private bool _campaignBotFinished;

    private void PumpCampaignBot(double now)
    {
        if (!CampaignBotEnabled || _campaignBotFinished) return;
        if (_campaignBotReadyAt < 0)
        {
            if (_phase != SessionPhase.InMatch || _peers.Count(p => p.MatchReady && p.SceneLoaded) != 7) return;
            _campaignBotReadyAt = now;
            var command = new LobbyCommandPacket { Hunter = (byte)Hunter.Sylux, TeamIndex = -1, BotLevel = 1 };
            int slot = NextFreeSlot();
            var result = ConfigureBot(command, update: false, out string reason);
            if (result != LobbyResultCode.Ok)
            {
                Log($"[net2] bot fill failed: {result} {reason}");
                _campaignBotFinished = true;
                return;
            }
            _campaignBotSlot = slot;
            BroadcastRoster();
            Log($"[net2] bot fill started slot={slot}");
        }
        if (now - _campaignBotReadyAt < 10) return;
        RemoveBot(_campaignBotSlot);
        BroadcastRoster();
        _campaignBotFinished = true;
        Log("[net2] bot fill complete; slot available for late join");
    }
}
