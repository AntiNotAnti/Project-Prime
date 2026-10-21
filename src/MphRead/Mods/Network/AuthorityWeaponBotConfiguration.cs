using System;
using MphRead.Mods.CustomWeapons;

namespace MphRead.Mods.Network;

public sealed partial class DedicatedServer
{
    private bool _autonomousWeaponBots;
    // Operator configuration before socket startup; no network client can call it.
    public void ConfigureAuthorityWeaponBots(int count)
    {
        if (_running || _weaponPackages == null || !CustomAuthorityCombatAdapter.AuthorityBotsEnabled
            || count < 1 || count > _maxPlayers || count > 8 || _bots.Count != 0)
            throw new InvalidOperationException("Configure 1–8 experimental bots after selecting an exact installed package, before starting the server.");
        for (int slot = 0; slot < count; slot++)
        {
            _slotGenerations[slot] = NetLifecycleTracker.Next(_slotGenerations[slot]);
            _bots.Add(new ServerBotSlot { SlotIndex = slot, Hunter = (byte)(slot % 7), BotLevel = 2, TeamIndex = -1 });
        }
        _autonomousWeaponBots = true; _botAssistedMatch = true;
        _sessionPolicy = ServerSessionPolicy.Continuous;
    }
}
