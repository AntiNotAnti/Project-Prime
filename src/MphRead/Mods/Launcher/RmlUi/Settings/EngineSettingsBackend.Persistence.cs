#if (MPHREAD_RMLUI_POC || MPHREAD_RMLUI) && !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MphRead.Mods.Render.Hud;
using MphRead.Mods.Settings;

namespace MphRead.Mods.Launcher.RmlUi.Settings;
internal sealed partial class EngineSettingsBackend
{
    private static string MenuPath=>Path.GetFullPath(Path.Combine("Savedata","settings.json"));
    private static string HudPath=>Path.GetFullPath(Path.Combine("Savedata","hud-profiles","active.json"));
    private static string LauncherPath=>Path.Combine(LauncherPrefs.Directory,"launcher.txt");
    private static string CommunityPath=>Path.Combine(LauncherPrefs.Directory,"map-community.txt");
    private static string ControlsPath=>Path.Combine(LauncherPrefs.Directory,"controls.txt");
    private Dictionary<string,byte[]?> BackupStores()
    {
        var backups=new Dictionary<string,byte[]?>(StringComparer.Ordinal);
        foreach(string path in new[]{MenuPath,LauncherPath,ControlsPath,CommunityPath,HudPath,HudPath+".bak"})
            backups[path]=File.Exists(path)?File.ReadAllBytes(path):null;
        return backups;
    }
    private void VerifySaved(IReadOnlyDictionary<string,string> values)
    {
        RefreshController();
        JsonObject saved=(JsonObject)JsonNode.Parse(File.ReadAllText(MenuPath))!;
        JsonObject menu=(JsonObject)saved["MenuSettings"]!;
        var launcher=PreferenceText.Parse(File.ReadAllText(LauncherPath),launcher:true);
        var controls=PreferenceText.Parse(File.ReadAllText(ControlsPath),launcher:false);
        foreach(Field field in _fields)
        {
            string expected=values[field.Definition.Id];string? actual=null;
            if(field.Store=="community")actual=File.ReadAllText(CommunityPath).Trim();
            if(field.Store=="menu")actual=menu[field.Key]!.GetValue<string>();
            if(field.Store=="launcher")launcher.TryGetValue(field.Key,out actual);
            if(field.Store=="controls")controls.TryGetValue(field.Key,out actual);
            if(field.Store=="binding")
            {
                var parts=expected.Split(':');expected=parts[0] switch {"Mouse"=>"Mouse:"+parts[2],"ScrollUp"=>"ScrollUp","ScrollDown"=>"ScrollDown",_=>"Key:"+parts[1]};
                controls.TryGetValue(field.Key,out actual);
            }
            if(field.Store=="")continue;
            if(field.Key=="window_mode")expected=WindowMode.Serialize(LauncherPrefs.WindowMode);
            if(field.Store=="controls"&&field.Key.StartsWith("pad_",StringComparison.Ordinal))
            {
                // Slot edits recompute the compatibility aggregate binding.
                expected=_padValues.GetValueOrDefault(field.Key,expected);
            }
            bool equivalent=string.Equals(actual,expected,StringComparison.OrdinalIgnoreCase);
            if(!equivalent&&float.TryParse(actual,NumberStyles.Float,Invariant,out float a)
                &&float.TryParse(expected,NumberStyles.Float,Invariant,out float b))equivalent=Math.Abs(a-b)<=.00051f;
            if(!equivalent)throw new IOException("The settings writer did not persist "+field.Definition.Label+".");
        }
        HudProfileStore.Parse(File.ReadAllText(HudPath));
    }
    private static void PreserveUnknownPreferences(Dictionary<string,byte[]?> before)
    {
        if(before[LauncherPath] is byte[] bytes)
        {
            var current=PreferenceText.Parse(File.ReadAllText(LauncherPath),launcher:true);
            var old=Encoding.UTF8.GetString(bytes).Split('\n');
            var unknown=old.Where(line=>{int split=line.IndexOf('=');return split>0&&!current.ContainsKey(line[..split].Trim());}).ToArray();
            // Compatibility aliases must be read before the current canonical
            // values, so preserving an older key cannot undo this transaction.
            if(unknown.Length>0)File.WriteAllLines(LauncherPath,unknown.Concat(File.ReadAllLines(LauncherPath)));
        }
        if(before[MenuPath] is byte[] menuBytes)
        {
            JsonObject old=(JsonObject)JsonNode.Parse(menuBytes)!;
            JsonObject current=(JsonObject)JsonNode.Parse(File.ReadAllText(MenuPath))!;
            Preserve(old,current);
            File.WriteAllText(MenuPath,current.ToJsonString(new JsonSerializerOptions{WriteIndented=true}));
        }
        static void Preserve(JsonObject source,JsonObject target)
        {
            foreach(var pair in source)
                if(!target.ContainsKey(pair.Key))target[pair.Key]=pair.Value?.DeepClone();
                else if(pair.Value is JsonObject oldObject&&target[pair.Key] is JsonObject newObject)Preserve(oldObject,newObject);
        }
    }
}
#endif
