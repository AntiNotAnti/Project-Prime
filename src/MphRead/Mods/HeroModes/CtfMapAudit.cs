using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using MphRead.Mods.Multiplayer;

namespace MphRead.Mods.HeroModes;
public static class CtfMapAudit
{
    // Read-only audit: uses the same entity reader and layer/capability policy as room loading.
    // Run again alongside an installation's real paths.txt. Never guesses support from map names.
    public static int Run(string output)
    {
        var rows=new List<object>();int inspected=0,metadataRejected=0,unavailable=0,compatible=0;
        foreach(var pair in Metadata.RoomMetadata.Where(p=>Metadata.IsBuiltInRoom(p.Key)&&p.Value.Multiplayer&&!p.Value.FirstHunt).OrderBy(p=>p.Key))
        {
            var checks=new List<object>();bool originalCompatible=false;
            foreach(int players in new[]{2,4,6,8})
            {
                var profile=MatchWorldProfile.Resolve(players);
                bool original=MapModeCapabilities.Supports(pair.Key,GameMode.Capture,profile,out string originalReason,players);
                bool ctf=MapModeCapabilities.Supports(pair.Key,GameMode.CaptureTheFlag,profile,out string ctfReason,players);
                bool missing=originalReason.StartsWith("Cannot inspect",StringComparison.Ordinal);
                bool metadataOnly=pair.Value.EntityPath==null;
                if(missing) unavailable++;else if(metadataOnly)metadataRejected++;else inspected++;
                originalCompatible|=original;
                checks.Add(new{players,original_capture=original,ctf,availability=missing?"UNVERIFIED_MISSING_ASSETS":metadataOnly?"UNSUPPORTED_BY_METADATA":"ENTITY_DATA_INSPECTED",originalReason,ctfReason});
            }
            if(originalCompatible)compatible++;
            rows.Add(new{room_key=pair.Key,display_name=pair.Value.InGameName,entity_path=pair.Value.EntityPath,checks});
        }
        var report=new{source="Official v0.1.53 + custom v19 + CTF development source",native_map_candidates=rows.Count,
            inspected_layer_cases=inspected,metadata_rejected_layer_cases=metadataRejected,unavailable_layer_cases=unavailable,verified_capture_compatible_maps=compatible,
            complete=unavailable==0,geometry_or_collision_playtested=false,maps=rows};
        string json=JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true});
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);File.WriteAllText(output,json);Console.WriteLine(json);
        return unavailable==0?0:2;
    }
}
