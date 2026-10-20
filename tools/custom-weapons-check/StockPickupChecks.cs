using MphRead;
using MphRead.Mods.CustomWeapons;
using MphRead.Mods.Launcher;

// Authored catalog/preflight contracts only, not proof of collecting all maps.
internal static class StockPickupChecks
{
    internal static void Run(Action<bool,string> check)
    {
        string[] rooms={"AD1 TRANSFER LOCK BT","AD1 TRANSFER LOCK DM","AD2 ALINOS PERCH","AD2 MAGMA VENTS",
            "CTF1 FAULT LINE - EXPANDED","CTF1_FAULT LINE","E3 FIRST HUNT","Gorea Prison","MP1 SANCTORUS",
            "MP10 OVERLOAD","MP11 BREAKTHROUGH","MP12 SIC TRANSIT","MP13 ACCELERATOR","MP14 OUTER REACH",
            "MP2 HARVESTER","MP3 PROVING GROUND","MP4 HIGHGROUND","MP4 HIGHGROUND - EXPANDED","MP5 FUEL SLUICE",
            "MP6 HEADSHOT","MP7 PROCESSOR CORE","MP8 FIRE CONTROL","MP9 CRYOCHASM","UNIT 3 VESPER STARPORT",
            "UNIT 4 ARCTERRA BASE","UNIT1 ALINOS LANDFALL","UNIT2 LANDING BAY"};
        check(rooms.Length==27&&rooms.Distinct(StringComparer.OrdinalIgnoreCase).Count()==27,"stock pickup catalog fixture pins27distinct stock rooms");
        foreach(string room in rooms)
        {
            var placements=StockRiftlinePickups.ForRoom(room);
            check(Metadata.IsBuiltInRoom(room)&&!Metadata.RoomMetadata[room].FirstHunt
                &&StockRiftlinePickups.Supports(room)&&StockRiftlinePickups.Supports(room.ToLowerInvariant())
                &&StockRiftlinePickups.TryPosition(room,out var authored)&&placements.Length==1
                &&placements[0].X4096==authored.X&&placements[0].Y4096==authored.Y&&placements[0].Z4096==authored.Z
                &&placements[0].SpawnId==1&&placements[0].Weapon==new WeaponRuntimeId(10)
                &&placements[0].InitialDelayTicks==0&&placements[0].RespawnTicks==1800&&placements[0].ReserveRefill==16,
                "authored stock M7 factory has one bounded independent spawn: "+room);
            placements[0]=default;
            check(StockRiftlinePickups.ForRoom(room)[0].SpawnId==1,"stock pickup factory returns detached placement arrays: "+room);
        }
        const string unknown="CUSTOM ROOM WITHOUT M7 LAYOUT";
        bool refused=false;try{StockRiftlinePickups.ForRoom(unknown);}catch(InvalidOperationException){refused=true;}
        check(refused&&!StockRiftlinePickups.Supports(unknown)&&!StockRiftlinePickups.TryPosition(unknown,out _),
            "unsupported custom-room name cannot synthesize runtime placement");
        var flagged=OfflineLaunch.Preview(new MenuSettings{RiftlineM7Available=true},unknown,GameMode.Battle,Hunter.Samus,0,0,0);
        check(!OfflineMatchPreflight.TryAdmit(flagged,out string reason)&&reason.Contains("27 stock",StringComparison.Ordinal),
            "public M7 preflight refuses unsupported custom-room layout before map preparation");
        var native=flagged with{MatchRules=flagged.MatchRules with{RiftlineM7Available=false}};
        bool nativeAdmitted=OfflineMatchPreflight.TryAdmit(native,out string nativeReason);
        check(!nativeAdmitted&&!nativeReason.Contains("Riftline",StringComparison.OrdinalIgnoreCase)
            &&nativeReason.Length>0,"native-only unknown-room fixture retains generic map admission instead of M7 layout gate");
        // A real custom-map launch remains covered by its separate geometry/package acceptance.
    }
}
