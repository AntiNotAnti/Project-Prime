using System;
using System.Collections.Generic;
using System.Linq;
using MphRead;

namespace MphRead.Mods.Launcher.Gui;

public sealed record PrimeWeaponDetails(BeamType Beam, string Name, string Description,
    string AffinityHunters, ushort UnchargedDamage, ushort ChargedDamage,
    ushort AmmoCost, int ChargedSpeed, ushort ChargedProjectiles);

/// <summary>
/// Read-only Armory projection over canonical local weapon metadata. It does
/// not invent levels, skins, currency, or progression.
/// </summary>
public sealed class ArmoryController
{
    public IReadOnlyList<PrimeWeaponDetails> Weapons { get; }

    public ArmoryController()
    {
        var entries = new List<PrimeWeaponDetails>();
        foreach (BeamType beam in Enum.GetValues<BeamType>())
        {
            if (beam < BeamType.PowerBeam || beam > BeamType.OmegaCannon) continue;
            if ((int)beam >= MphRead.Weapons.Current.Count
                || (int)beam >= Metadata.WeaponNames.Count) continue;
            WeaponInfo weapon = MphRead.Weapons.Current[(int)beam];
            string affinity = string.Join(", ", PlayableHunterCatalog.All
                .Where(hunter => MphRead.Weapons.GetAffinityBeam(hunter) == beam)
                .Select(hunter => hunter.ToString()));
            entries.Add(new PrimeWeaponDetails(beam, Metadata.WeaponNames[(int)beam],
                weapon.Description, affinity.Length == 0 ? "None" : affinity,
                weapon.UnchargedDamage, weapon.ChargedDamage, weapon.AmmoCost,
                weapon.ChargedSpeed, weapon.ChargedProjectiles));
        }
        Weapons = entries.AsReadOnly();
    }

    public PrimeWeaponDetails Get(BeamType beam)
        => Weapons.FirstOrDefault(weapon => weapon.Beam == beam)
            ?? throw new ArgumentOutOfRangeException(nameof(beam));
}
