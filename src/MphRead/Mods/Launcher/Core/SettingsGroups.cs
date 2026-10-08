using System;
using System.Text.RegularExpressions;
namespace MphRead.Mods.Launcher.Core;

// Stable groups are derived from authoritative field IDs, not translated labels.
internal static class SettingsGroups
{
    internal static string Words(string value) => Regex.Replace(value.Replace('_', ' '), "([a-z0-9])([A-Z])", "$1 $2");
    internal static string For(SettingsFieldDefinition field)
    {
        string id=field.Id;
        if(id.StartsWith("hud/",StringComparison.Ordinal)||id.StartsWith("hudOverride./",StringComparison.Ordinal))
        {
            string[] path=id[(id.IndexOf('/')+1)..].Split('/');
            if(path.Length==1)return "Appearance and scale";
            if(path[0]=="weaponCrosshairs")
            {
                string[] weapons={"Power Beam","Volt Driver","Missile","Battlehammer","Imperialist","Judicator","Magmaul","Shock Coil","Omega Cannon"};
                string weapon=int.TryParse(path[1],out int n)&&n<weapons.Length?weapons[n]:path[1];
                return "Crosshair / "+weapon;
            }
            if(path[0]=="elements"&&path.Length>1) return "HUD / "+Words(path[1].Replace("core.","").Replace("combat.",""));
            return "HUD / "+Words(path[0]);
        }
        if(id.StartsWith("legacy.Crosshair",StringComparison.Ordinal))return "HUD / crosshair";
        if(field.Category is SettingsCategory.Controls or SettingsCategory.Controller or SettingsCategory.Touch)
        {
            if(id.StartsWith("binding.",StringComparison.Ordinal))return "Keyboard bindings";
            if(id.StartsWith("pad.pad_",StringComparison.Ordinal))return "Controller / "+Words(id[8..].Split('_')[0]);
            if(id.StartsWith("pad.",StringComparison.Ordinal))
            {
                if(Regex.IsMatch(id,"gamepad_(look|scoped|invert|curve|accel|smoothing|turn)",RegexOptions.IgnoreCase))return "Controller / aiming";
                if(Regex.IsMatch(id,"gamepad_(lt|rt|trigger|left|right|calibr|radial)",RegexOptions.IgnoreCase))return "Controller / calibration";
                if(id.Contains("gyro",StringComparison.OrdinalIgnoreCase))return "Controller / gyro";
                if(id.Contains("stick",StringComparison.OrdinalIgnoreCase)||id.Contains("dead",StringComparison.OrdinalIgnoreCase))return "Controller / sticks";
                return "Controller / preferences";
            }
            if(id.StartsWith("touch.",StringComparison.Ordinal))return id.StartsWith("touch.Layout.",StringComparison.Ordinal)?"Touch / layout":"Touch / buttons";
            if(id.StartsWith("stylus.",StringComparison.Ordinal)||id.StartsWith("pointer.",StringComparison.Ordinal))return "Stylus";
            return "Mouse and aiming";
        }
        if(field.Category==SettingsCategory.Graphics)
        {
            if(Regex.IsMatch(id,"Light|Shadow|Occlusion|Reflection|Pbr",RegexOptions.IgnoreCase))return "Lighting and shadows";
            if(Regex.IsMatch(id,"Texture|Material|Replacement|Cosmetic",RegexOptions.IgnoreCase))return "Textures and models";
            if(Regex.IsMatch(id,"Fog|Bloom|Cel|Gamma|Saturation|Contrast|Sharpen|Glow",RegexOptions.IgnoreCase))return "Post processing";
            return "Quality and performance";
        }
        if(field.Category==SettingsCategory.Online)return "Online services";
        if(field.Category==SettingsCategory.Audio&&id.StartsWith("sound.",StringComparison.Ordinal))return "Combat sounds";
        if(field.Category==SettingsCategory.Replays&&id.StartsWith("input.",StringComparison.Ordinal))return "Playback shortcuts";
        return "General";
    }
}
