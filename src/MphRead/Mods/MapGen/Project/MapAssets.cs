using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MphRead.Mods.MapGen
{
    public sealed class MapAsset
    {
        public string? Name { get; set; }
        public string? SourcePath { get; set; }
        public string Path { get; set; } = "";
        public string Kind { get; set; } = "texture";
    }
    public sealed class MapAudioSettings
    {
        public string? Music { get; set; }
        public string? GameMusic { get; set; }
        public float Volume { get; set; } = .8f;
        public bool Loop { get; set; } = true;
    }
    public sealed class MapCapabilities
    {
        public List<string> SupportedModes { get; set; } = new() { "Battle", "Survival" };
        public int MinPlayers { get; set; } = 1;
        public int MaxPlayers { get; set; } = 8;
    }
    public static class MapAssets
    {
        public static byte[] Read(MapDefinition definition,string relative)
        {
            MapPackageReader.CanonicalName(relative);
            if(definition.BundlePath!=null)return MapBundle.ReadEntry(definition.BundlePath,relative)??throw new InvalidDataException("Missing map asset: "+relative);
            string root=Path.GetFullPath(definition.BaseDirectory??CustomRooms.MapDirectory);
            string full=Path.GetFullPath(Path.Combine(root,relative));
            if(!full.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Map asset escapes its project.");
            if(new FileInfo(full).Length>MapPackageReader.MaxEntryBytes)throw new InvalidDataException("Map asset is too large.");
            return File.ReadAllBytes(full);
        }
        public static void Validate(MapDefinition definition,MapValidationResult result,bool checkFiles)
        {
            if(definition.Assets==null){result.Error("FP-MAP-021","Asset list cannot be null.");return;}
            var paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var asset in definition.Assets)
            {
                try
                {
                    if(asset==null)throw new InvalidDataException("Null map asset.");
                    MapPackageReader.CanonicalName(asset.Path);
                    if(!paths.Add(asset.Path))throw new InvalidDataException("Duplicate map asset path.");
                    string ext=Path.GetExtension(asset.Path).ToLowerInvariant();
                    if(asset.Kind=="audio"&&ext is not(".wav" or ".ogg" or ".mp3"))throw new InvalidDataException("Music must be WAV, OGG or MP3.");
                    if(asset.Kind=="texture"&&ext is not(".png" or ".jpg" or ".jpeg" or ".tga" or ".tex"))throw new InvalidDataException("Unsupported map texture format.");
                    if(asset.Kind=="preview"&&ext!=".png")throw new InvalidDataException("Preview must be PNG.");
                    if(asset.Kind is not("audio" or "texture" or "preview"))throw new InvalidDataException("Unknown asset kind.");
                    if(checkFiles)
                    {
                        byte[] bytes=Read(definition,asset.Path);
                        if(asset.Kind=="preview"&&(bytes.Length<24||!bytes.AsSpan(0,8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10})
                            ||System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16)) is <1 or >4096
                            ||System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20)) is <1 or >4096))
                            throw new InvalidDataException("Preview must be a PNG no larger than 4096×4096.");
                    }
                }
                catch(Exception ex)when(ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException){result.Error("FP-MAP-021",ex.Message);}
            }
            if(definition.Import is { ModernTextures.Count: > 0 } import)
            {
                MapTexturePack? pack = null;
                if(checkFiles)
                {
                    try { pack = import.LoadTexturePack(); }
                    catch(Exception ex) when(ex is IOException or InvalidDataException or ProgramException)
                    { result.Error("FP-MAP-001","Q3 fallback texture pack is invalid: "+ex.Message); }
                }
                foreach(var pair in import.ModernTextures)
                {
                    string modern = pair.Value;
                    string ext = Path.GetExtension(modern).ToLowerInvariant();
                    if(pair.Key < 0) result.Error("FP-MAP-001","Q3 HD texture shader index cannot be negative.");
                    if(!paths.Contains(modern)||!definition.Assets.Any(a=>a?.Path==modern&&a.Kind=="texture"))
                        result.Error("FP-MAP-001","Q3 HD textures must reference declared texture assets.");
                    else if(ext is not(".png" or ".jpg" or ".jpeg" or ".tga"))
                        result.Error("FP-MAP-001","Q3 HD textures must be PNG, JPEG or TGA.");
                    else if(checkFiles)
                    {
                        try
                        {
                            byte[] bytes=Read(definition,modern);
                            if(Mods.Render.ModernTextureAsset.PortableEncodedExtension(bytes)==null)
                                throw new InvalidDataException("Unsupported portable HD texture encoding.");
                            Mods.Render.ModernTextureAsset.ProbeDimensions(bytes);
                        }
                        catch(Exception ex) when(ex is IOException or InvalidDataException or ArgumentException)
                        { result.Error("FP-MAP-001",ex.Message); }
                    }
                    if(pack!=null&&!pack.BySourceIndex.ContainsKey(pair.Key))
                        result.Warning("FP-MAP-006",$"Q3 HD texture shader {pair.Key} is no longer present in the fallback texture pack.");
                }
            }

            if(definition.Audio is {} audio)
            {
                if(!float.IsFinite(audio.Volume)||audio.Volume is <0 or >1)result.Error("FP-MAP-021","Music volume must be 0–1.");
                if(audio.Music!=null&&(!paths.Contains(audio.Music)||!definition.Assets.Any(a=>a?.Path==audio.Music&&a.Kind=="audio")))result.Error("FP-MAP-021","Music must reference an audio asset.");
                if(audio.Music!=null&&audio.GameMusic!=null)result.Error("FP-MAP-021","Choose custom music or game music, not both.");
                if(audio.GameMusic!=null&&(!Enum.TryParse<MusicId>(audio.GameMusic,true,out var music)||!Enum.IsDefined(music)))result.Error("FP-MAP-021","Unknown game music reference.");
            }
            foreach(var material in definition.Materials)
            {
                if(material?.Texture is {} texture)
                {
                    if(!paths.Contains(texture)||!texture.EndsWith(".tex",StringComparison.OrdinalIgnoreCase))result.Error("FP-MAP-001","Custom materials must reference a baked texture asset.",material.Id);
                    else if(checkFiles)
                    {
                        try{var pack=MapTexturePack.Load(Read(definition,texture),texture);if(pack.Entries.Count!=1)result.Error("FP-MAP-001","Native materials require a single texture.",material.Id);}
                        catch(Exception ex)when(ex is IOException or InvalidDataException or ProgramException){result.Error("FP-MAP-001",ex.Message,material.Id);}
                    }
                }
                foreach(string modern in new[]{material?.Albedo,material?.Normal,material?.SpecularRoughness,material?.Emissive}.Where(path=>!String.IsNullOrEmpty(path)).Select(path=>path!))
                {
                    string ext=Path.GetExtension(modern).ToLowerInvariant();
                    if(!paths.Contains(modern)||!definition.Assets.Any(a=>a?.Path==modern&&a.Kind=="texture"))
                        result.Error("FP-MAP-001","HD material channels must reference declared texture assets.",material?.Id);
                    else if(ext is not(".png" or ".jpg" or ".jpeg" or ".tga"))
                        result.Error("FP-MAP-001","HD material channels must be PNG, JPEG or TGA.",material?.Id);
                    else if(checkFiles)
                    {
                        try
                        {
                            byte[] bytes=Read(definition,modern);
                            if(Mods.Render.ModernTextureAsset.PortableEncodedExtension(bytes)==null)
                                throw new InvalidDataException("Unsupported portable HD texture encoding.");
                            Mods.Render.ModernTextureAsset.ProbeDimensions(bytes);
                        }
                        catch(Exception ex)when(ex is IOException or InvalidDataException or ArgumentException){result.Error("FP-MAP-001",ex.Message,material?.Id);}
                    }
                }
            }
        }
    }
    public static class MapModeValidator
    {
        public static string? WhyUnsupported(MapDefinition definition,GameMode mode,int players=1)
        {
            if(definition.Capabilities is not {} capabilities)return null;
            if(capabilities.SupportedModes==null||!capabilities.SupportedModes.Any(m => string.Equals(m, mode.ToString(), StringComparison.OrdinalIgnoreCase) || mode == GameMode.Battle && string.Equals(m, "InstaGib", StringComparison.OrdinalIgnoreCase)))return $"{definition.Name} does not support {mode}.";
            if(players<capabilities.MinPlayers||players>capabilities.MaxPlayers)return $"{definition.Name} supports {capabilities.MinPlayers}–{capabilities.MaxPlayers} players.";
            return null;
        }
        public static void Validate(MapDefinition d,MapValidationResult r)
        {
            if (d.HardpointOrder == null || d.HardpointOrder.Count > 512
                || d.HardpointOrder.Any(id => id < 0 || id > short.MaxValue)
                || d.HardpointOrder.Distinct().Count() != d.HardpointOrder.Count)
                r.Error("FP-MAP-022", "Hardpoint order must contain unique objective entity IDs.");
            if(d.Capabilities is not {} c)return;
            if(c.MinPlayers<1||c.MaxPlayers>8||c.MinPlayers>c.MaxPlayers)r.Error("FP-MAP-022","Player range must be within 1–8.");
            if(c.SupportedModes==null||c.SupportedModes.Count==0){r.Error("FP-MAP-022","Choose at least one supported mode.");return;}
            foreach(string mode in c.SupportedModes)
            {
                if(!Enum.TryParse<GameMode>(mode,true,out var parsed)||!Enum.IsDefined(parsed)){r.Error("FP-MAP-022","Unknown game mode: "+mode);continue;}
                if (parsed == GameMode.InstaGib) parsed = GameMode.Battle;
                if(mode.Contains("Teams",StringComparison.OrdinalIgnoreCase)&&(!d.Spawns.Any(s=>s?.Team==0)||!d.Spawns.Any(s=>s?.Team==1)))r.Error("FP-MAP-022","Team modes require spawns for both teams.");
                if(parsed is not(GameMode.Battle or GameMode.BattleTeams or GameMode.Survival or GameMode.SurvivalTeams or GameMode.PrimeHunter or GameMode.OneInTheChamber or GameMode.GunGame or GameMode.KillConfirmed or GameMode.KillConfirmedTeams) && d.NativeRoom?.PreserveEntities != true)
                    r.Error("FP-MAP-022",$"{mode} requires objective entities that this source does not supply.");
            }
        }
    }
}
