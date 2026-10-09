using System;
using System.Net;
using MphRead.Entities;

namespace MphRead.Mods.Network;

/// <summary>Explicit operator-issued loadout for private loopback acceptance.
/// Never infers ownership from client weapon/ammo fields. Not enabled in ordinary servers.</summary>
internal static class NetCombatFixture
{
    private static BeamType _weapon=BeamType.None;
    private static readonly ShotKey[] Lives=new ShotKey[8];
    private static readonly uint[] Grants=new uint[8];
    internal static bool Configure(string value,bool dedicated,bool unlisted)
    {
        _weapon=BeamType.None;Array.Clear(Lives);Array.Clear(Grants);
        if(!dedicated || !unlisted || !Enum.TryParse(value,true,out BeamType weapon)
            || weapon<BeamType.PowerBeam || weapon>BeamType.OmegaCannon) return false;
        _weapon=weapon;return true;
    }
    internal static bool Allows(IPAddress address)
        => _weapon!=BeamType.None && IPAddress.IsLoopback(address);
    internal static void Apply(int slot,IPAddress address)
    {
        if(!NetSession.IsServer || !Allows(address) || (uint)slot>=8 || slot>=PlayerEntity.Players.Count) return;
        var player=PlayerEntity.Players[slot];
        if(player==null || !player.LoadFlags.TestFlag(LoadFlags.Active) || !player.ModIsInPlay) return;
        ShotKey life=ShotKey.For(slot,0);
        if(Lives[slot]==life && NetSession.NetFrame-Grants[slot]<60) return;
        bool newLife=Lives[slot]!=life;
        // Fixture administration uses the same local issuance helper as weapon
        // and firing-context checks. Native cadence, spending and proof still apply.
        player.ModArmWeapon(_weapon);
        Lives[slot]=life;Grants[slot]=NetSession.NetFrame;
        if(newLife) Console.WriteLine($"[combat-fixture] loopback slot={slot} life={life.LifeId} weapon={_weapon}; authority-issued loadout, refresh=60 frames");
    }
    internal static void Disable() { _weapon=BeamType.None;Array.Clear(Lives);Array.Clear(Grants); }
}
