using System;
using OpenTK.Mathematics;

namespace MphRead.Mods.CustomWeapons;

// Authored additional centres, in4096 units. Native walking/contact and separation
// evidence: docs/custom-weapons/05n-offline-pickups.md. Runtime never searches,
// moves or replaces a native pickup/player spawn. No external file is loaded.
internal static class StockRiftlinePickups
{
    internal const uint RespawnTicks=1800;
    internal static bool Supports(string roomKey)=>TryPosition(roomKey,out _);
    internal static CustomPickupPlacement[] ForRoom(string roomKey)
    {
        if (!TryPosition(roomKey,out var point))
            throw new InvalidOperationException("Riftline M7 pickups require an authored stock multiplayer map layout.");
        return new[] { new CustomPickupPlacement(1,new(10),point.X,point.Y,point.Z,0,RespawnTicks,16) };
    }
    internal static CustomPickupPlacement[] ForRoom(string roomKey, WeaponDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var placements = ForRoom(roomKey);
        for (int i = 0; i < placements.Length; i++)
            placements[i] = placements[i] with { ReserveRefill = Math.Min(placements[i].ReserveRefill, definition.Mechanics.MaximumReserve) };
        return placements;
    }
    internal static bool TryPosition(string roomKey,out Vector3i position)
    {
        Vector3i? point=roomKey?.ToUpperInvariant() switch
        {
            "AD1 TRANSFER LOCK BT" => new Vector3i(163486,68039,-121219),
            "AD1 TRANSFER LOCK DM" => new Vector3i(14840,23389,58500),
            "AD2 ALINOS PERCH" => new Vector3i(-89714,2662,63181),
            "AD2 MAGMA VENTS" => new Vector3i(-89714,2662,63181),
            "CTF1 FAULT LINE - EXPANDED" => new Vector3i(-235371,62054,-41226),
            "CTF1_FAULT LINE" => new Vector3i(-235371,62054,-41226),
            "E3 FIRST HUNT" => new Vector3i(-73649,12902,-82863),
            "GOREA PRISON" => new Vector3i(-45061,38679,-45842),
            "MP1 SANCTORUS" => new Vector3i(6449,18398,-72283),
            "MP10 OVERLOAD" => new Vector3i(-24391,2662,579),
            "MP11 BREAKTHROUGH" => new Vector3i(-35166,23770,21634),
            "MP12 SIC TRANSIT" => new Vector3i(-51102,2869,-74017),
            "MP13 ACCELERATOR" => new Vector3i(11066,-50599,-74397),
            "MP14 OUTER REACH" => new Vector3i(-43100,21753,-42917),
            "MP2 HARVESTER" => new Vector3i(-92478,78179,-206504),
            "MP3 PROVING GROUND" => new Vector3i(5044,2662,-82087),
            "MP4 HIGHGROUND" => new Vector3i(-49118,21215,57350),
            "MP4 HIGHGROUND - EXPANDED" => new Vector3i(54578,50155,-55448),
            "MP5 FUEL SLUICE" => new Vector3i(35019,2662,18431),
            "MP6 HEADSHOT" => new Vector3i(153618,100175,154010),
            "MP7 PROCESSOR CORE" => new Vector3i(24333,10667,44694),
            "MP8 FIRE CONTROL" => new Vector3i(31673,19113,38992),
            "MP9 CRYOCHASM" => new Vector3i(-34537,84382,24170),
            "UNIT 3 VESPER STARPORT" => new Vector3i(-14,31323,-147739),
            "UNIT 4 ARCTERRA BASE" => new Vector3i(61410,79859,-51152),
            "UNIT1 ALINOS LANDFALL" => new Vector3i(109473,8116,-120339),
            "UNIT2 LANDING BAY" => new Vector3i(61451,2662,62762),
            _=>null
        };
        position=point.GetValueOrDefault();return point.HasValue;
    }
}
