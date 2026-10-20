using System;
using System.IO;
using System.Linq;
using MphRead;
using MphRead.Editor;
using MphRead.Mods;
using MphRead.Mods.Multiplayer;
using MphRead.Utility;
using OpenTK.Mathematics;

internal static class OfflineArenaInstallChecks
{
    internal static void Run()
    {
        // AD2 is a real built-in metadata entry, but some installations do not
        // contain its corresponding entity file. The test uses only generated
        // data, never user-owned game assets.
        const string arena = "AD2 ALINOS PERCH";
        string originalKey = Paths.MphKey;
        string originalRoot = Paths.FileSystem;
        string fixture = Directory.CreateTempSubdirectory("prime-offline-map-").FullName;
        string other = Directory.CreateTempSubdirectory("prime-offline-other-").FullName;
        try
        {
            Paths.SetPath(originalKey, fixture);
            string entity = Paths.Combine(fixture, Metadata.RoomMetadata[arena].EntityPath!);

            Check(!ThumbnailGenerator.MultiplayerRooms().Contains(arena, StringComparer.OrdinalIgnoreCase),
                "missing AD2 entity layer is not offered as a playable bot arena");
            Check(!MapModeCapabilities.Supports(arena, GameMode.Battle, MatchWorldProfile.Resolve(2),
                    out string missing, configuredPlayers: 2, allowLocalSource: true)
                && missing.Contains("ad2_dm1_Ent.bin", StringComparison.Ordinal)
                && missing.Contains("missing extracted arena entity data", StringComparison.Ordinal),
                "missing entity data returns a repairable, specific validation error");

            Directory.CreateDirectory(Path.GetDirectoryName(entity)!);
            byte[] valid = Repack.PackEntities(new EntityEditorBase[]
            {
                new PlayerSpawnEntityEditor
                {
                    Id = 1, LayerMask = ushort.MaxValue, Active = true,
                    Up = Vector3.UnitY, Facing = Vector3.UnitZ, NodeName = "rmMain"
                }
            });
            File.WriteAllBytes(entity, valid);
            Check(ThumbnailGenerator.MultiplayerRooms().Contains(arena, StringComparer.OrdinalIgnoreCase)
                && MapModeCapabilities.Supports(arena, GameMode.Battle, MatchWorldProfile.Resolve(2),
                    out _, configuredPlayers: 2, allowLocalSource: true),
                "repairing the file restores the arena and native spawn validation");

            File.Delete(entity);
            Check(!ThumbnailGenerator.MultiplayerRooms().Contains(arena, StringComparer.OrdinalIgnoreCase),
                "removing an already cached playable entity file invalidates the arena");

            File.WriteAllBytes(entity, valid);
            Check(ThumbnailGenerator.MultiplayerRooms().Contains(arena, StringComparer.OrdinalIgnoreCase),
                "same-process restoration does not require returning through the Play tab");

            Paths.SetPath(originalKey, other);
            Check(!ThumbnailGenerator.MultiplayerRooms().Contains(arena, StringComparer.OrdinalIgnoreCase),
                "cached spawn results never survive a different configured game-file root");
        }
        finally
        {
            Paths.SetPath(originalKey, originalRoot);
            Paths.MphKey = originalKey;
            Directory.Delete(fixture, true);
            Directory.Delete(other, true);
        }
    }

    private static void Check(bool valid, string name)
    {
        if (!valid) throw new InvalidOperationException(name);
        Console.WriteLine("OFFLINE ARENA INSTALL PASS " + name);
    }
}
