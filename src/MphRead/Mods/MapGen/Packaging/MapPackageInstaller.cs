using System;
using System.IO;
using System.Linq;

namespace MphRead.Mods.MapGen
{
    public static class MapPackageInstaller
    {
        public static MapDefinition Install(string download,Guid id,string contentHash,string archiveHash,string library)
        {
            if(MapBuildFingerprint.HashFile(download)!=archiveHash)throw new InvalidDataException("Downloaded package hash does not match the server.");
            using(var package=new MapPackageReader(download))
            {
                if(package.Manifest?.MapId!=id||package.Manifest.ContentHash!=contentHash)throw new InvalidDataException("Downloaded map identity does not match the server.");
            }
            var definition=MapDefinition.Load(download);
            if (Metadata.IsBuiltInRoom(definition.Name)) throw new InvalidDataException("A custom map cannot replace a built-in room.");
            var conflict = CustomRooms.Definitions.FirstOrDefault(d => d.Name.Equals(definition.Name, StringComparison.OrdinalIgnoreCase) && (d.MapId == Guid.Empty ? MapPackageBuilder.LegacyId(d.Name) : d.MapId) != definition.MapId);
            if (conflict != null) throw new InvalidDataException("Another map already uses this runtime name. Rename the map before sharing.");
            var build = MapBuildScheduler.Shared.BuildAsync(MapBuildSnapshot.Capture(definition)).GetAwaiter().GetResult();
            MapCompiler.ThrowIfInvalid(build.Validation());
            Directory.CreateDirectory(library);
            string destination = Path.Combine(library, id.ToString("N") + MapBundle.Extension);
            AtomicFile.Write(destination, File.ReadAllBytes(download));
            var installed = MapDefinition.Load(destination);
            MapBuildScheduler.Install(build, installed, CustomRooms.ArchiveDirectory(installed),
                CustomRooms.EntityDirectory(), CustomRooms.NodeDirectory());
            return installed;
        }
    }
}
