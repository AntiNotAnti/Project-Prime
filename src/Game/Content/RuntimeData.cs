using System;
using System.Collections.Frozen;

namespace MphRead
{
    public static class RuntimeData
    {
        public sealed class RomDataValues
        {
            public string File { get; }
            public int Offset { get; }
            public int Size { get; }

            public RomDataValues(string file, int offset, int size)
            {
                File = file;
                Offset = offset;
                Size = size;
            }
        }

        private class RomData
        {
            public RomDataValues FontModel { get; init; } = null!;
            public RomDataValues FontWidths { get; init; } = null!;
            public RomDataValues FontOffsets { get; init; } = null!;
            public RomDataValues FontCharData { get; init; } = null!;
            public RomDataValues TerrianSfx { get; init; } = null!;
            public RomDataValues BeamSfx { get; init; } = null!;
            public RomDataValues HunterSfx { get; init; } = null!;
            public RomDataValues EnemyDamageSfx { get; init; } = null!;
            public RomDataValues EnemyDeathSfx { get; init; } = null!;
            public RomDataValues PlatformSfx { get; init; } = null!;
        }

        public static RomDataValues? GetFontModel(string version)
            => _romData.TryGetValue(version, out RomData? data) ? data.FontModel : null;

        private static long _loadedGeneration = -1;

        public static void Load()
        {
            lock (ContentEnvironment.SyncRoot)
            {
                if (_loadedGeneration == ContentEnvironment.Generation) { return; }
                // Font/audio tables belong to the client startup context. A
                // running worker must never see them replaced by another load.
                ContentEnvironment.RequireMutableContext();
                LoadCore();
                _loadedGeneration = ContentEnvironment.Generation;
            }
        }

        private static void LoadCore()
        {
            if (!_romData.TryGetValue(Paths.MphKey, out RomData? data))
            {
                return;
            }
            // arm9.bin
            byte[] bytes = ContentFiles.ReadBytes(Paths.Combine(Paths.FileSystem, "_bin", data.FontWidths.File));
            byte[] widths = bytes[data.FontWidths.Offset..(data.FontWidths.Offset + data.FontWidths.Size)];
            byte[] offsets = bytes[data.FontOffsets.Offset..(data.FontOffsets.Offset + data.FontOffsets.Size)];
            byte[] chars = bytes[data.FontCharData.Offset..(data.FontCharData.Offset + data.FontCharData.Size)];
            byte[] enemyDamageSfx = bytes[data.EnemyDamageSfx.Offset..(data.EnemyDamageSfx.Offset + data.EnemyDamageSfx.Size)];
            byte[] enemyDeathSfx = bytes[data.EnemyDeathSfx.Offset..(data.EnemyDeathSfx.Offset + data.EnemyDeathSfx.Size)];
            Text.Font.Normal.SetData(widths, offsets, chars, minChar: 32);
            // overlay9_2
            bytes = ContentFiles.ReadBytes(Paths.Combine(Paths.FileSystem, "_bin", data.BeamSfx.File));
            byte[] terrainSfx = bytes[data.TerrianSfx.Offset..(data.TerrianSfx.Offset + data.TerrianSfx.Size)];
            byte[] beamSfx = bytes[data.BeamSfx.Offset..(data.BeamSfx.Offset + data.BeamSfx.Size)];
            byte[] hunterSfx = bytes[data.HunterSfx.Offset..(data.HunterSfx.Offset + data.HunterSfx.Size)];
            Metadata.SetTerrainSfxData(terrainSfx);
            Metadata.SetBeamSfxData(beamSfx);
            Metadata.SetHunterSfxData(hunterSfx);
            Metadata.SetEnemyDamageSfxData(enemyDamageSfx);
            Metadata.SetEnemyDeathSfxData(enemyDeathSfx);
            // overlay9_15 (or overlay9_12 for A76E0)
            bytes = ContentFiles.ReadBytes(Paths.Combine(Paths.FileSystem, "_bin", data.PlatformSfx.File));
            byte[] platformSfx = bytes[data.PlatformSfx.Offset..(data.PlatformSfx.Offset + data.PlatformSfx.Size)];
            Metadata.SetPlatformSfxData(platformSfx);
        }

        private static readonly FrozenDictionary<string, RomData> _romData = Frozen.Create<string, RomData>(
        [
            new(
                Ver.A76E0,
                new RomData()
                {
                    FontModel = new RomDataValues("arm9.bin", 0x9D528, 0x8284),
                    FontWidths = new RomDataValues("arm9.bin", 0x95C68, 480),
                    FontOffsets = new RomDataValues("arm9.bin", 0x95A88, 480),
                    FontCharData = new RomDataValues("arm9.bin", 0x96348, 0x4000),
                    TerrianSfx = new RomDataValues("overlay9_2", 0x1D828, 144),
                    BeamSfx = new RomDataValues("overlay9_2", 0x1D8B8, 180),
                    HunterSfx = new RomDataValues("overlay9_2", 0x1D96C, 272),
                    EnemyDamageSfx = new RomDataValues("arm9.bin", 0x9B574, 208),
                    EnemyDeathSfx = new RomDataValues("arm9.bin", 0x9B644, 208),
                    PlatformSfx = new RomDataValues("overlay9_12", 0x81E4, 360)
                }
            ),
            new (
                Ver.AMHE0,
                new RomData()
                {
                    FontModel = new RomDataValues("arm9.bin", 0xC76D4, 0x8284),
                    FontWidths = new RomDataValues("arm9.bin", 0xBF9B0, 480),
                    FontOffsets = new RomDataValues("arm9.bin", 0xBFB90, 480),
                    FontCharData = new RomDataValues("arm9.bin", 0xC0270, 0x4000),
                    TerrianSfx = new RomDataValues("overlay9_2", 0x1DA08, 144),
                    BeamSfx = new RomDataValues("overlay9_2", 0x1DA98, 180),
                    HunterSfx = new RomDataValues("overlay9_2", 0x1DB4C, 272),
                    EnemyDamageSfx = new RomDataValues("arm9.bin", 0xC54A8, 208),
                    EnemyDeathSfx = new RomDataValues("arm9.bin", 0xC5578, 208),
                    PlatformSfx = new RomDataValues("overlay9_15", 0x8284, 360)
                }
            ),
            new (
                Ver.AMHE1,
                new RomData()
                {
                    FontModel = new RomDataValues("arm9.bin", 0xC7F5C, 0x8284),
                    FontWidths = new RomDataValues("arm9.bin", 0xC020C, 480),
                    FontOffsets = new RomDataValues("arm9.bin", 0xC03EC, 480),
                    FontCharData = new RomDataValues("arm9.bin", 0xC0ACC, 0x4000),
                    TerrianSfx = new RomDataValues("overlay9_2", 0x1DA68, 144),
                    BeamSfx = new RomDataValues("overlay9_2", 0x1DAF8, 180),
                    HunterSfx = new RomDataValues("overlay9_2", 0x1DBAC, 272),
                    EnemyDamageSfx = new RomDataValues("arm9.bin", 0xC5D30, 208),
                    EnemyDeathSfx = new RomDataValues("arm9.bin", 0xC5E00, 208),
                    PlatformSfx = new RomDataValues("overlay9_15", 0x8284, 360)
                }
            ),
            new (
                Ver.AMHJ0,
                new RomData()
                {
                    FontModel = new RomDataValues("arm9.bin", 0xC9510, 0x8284),
                    FontWidths = new RomDataValues("arm9.bin", 0xC1754, 480),
                    FontOffsets = new RomDataValues("arm9.bin", 0xC1934, 480),
                    FontCharData = new RomDataValues("arm9.bin", 0xC2014, 0x4000),
                    TerrianSfx = new RomDataValues("overlay9_2", 0x1DA68, 144),
                    BeamSfx = new RomDataValues("overlay9_2", 0x1DAF8, 180),
                    HunterSfx = new RomDataValues("overlay9_2", 0x1DBAC, 272),
                    EnemyDamageSfx = new RomDataValues("arm9.bin", 0xC7278, 208),
                    EnemyDeathSfx = new RomDataValues("arm9.bin", 0xC7348, 208),
                    PlatformSfx = new RomDataValues("overlay9_15", 0x8284, 360)
                }
            ),
            new (
                Ver.AMHJ1,
                new RomData()
                {
                    FontModel = new RomDataValues("arm9.bin", 0xC94D0, 0x8284),
                    FontWidths = new RomDataValues("arm9.bin", 0xC1714, 480),
                    FontOffsets = new RomDataValues("arm9.bin", 0xC18F4, 480),
                    FontCharData = new RomDataValues("arm9.bin", 0xC1FD4, 0x4000),
                    TerrianSfx = new RomDataValues("overlay9_2", 0x1DA68, 144),
                    BeamSfx = new RomDataValues("overlay9_2", 0x1DAF8, 180),
                    HunterSfx = new RomDataValues("overlay9_2", 0x1DBAC, 272),
                    EnemyDamageSfx = new RomDataValues("arm9.bin", 0xC7238, 208),
                    EnemyDeathSfx = new RomDataValues("arm9.bin", 0xC7308, 208),
                    PlatformSfx = new RomDataValues("overlay9_15", 0x8284, 360)
                }
            ),
            new (
                Ver.AMHP0,
                new RomData()
                {
                    FontModel = new RomDataValues("arm9.bin", 0xC7F7C, 0x8284),
                    FontWidths = new RomDataValues("arm9.bin", 0xC022C, 480),
                    FontOffsets = new RomDataValues("arm9.bin", 0xC040C, 480),
                    FontCharData = new RomDataValues("arm9.bin", 0xC0AEC, 0x4000),
                    TerrianSfx = new RomDataValues("overlay9_2", 0x1DA08, 144),
                    BeamSfx = new RomDataValues("overlay9_2", 0x1DA98, 180),
                    HunterSfx = new RomDataValues("overlay9_2", 0x1DB4C, 272),
                    EnemyDamageSfx = new RomDataValues("arm9.bin", 0xC5D50, 208),
                    EnemyDeathSfx = new RomDataValues("arm9.bin", 0xC5E20, 208),
                    PlatformSfx = new RomDataValues("overlay9_15", 0x8284, 360)
                }
            ),
            new (
                Ver.AMHP1,
                new RomData()
                {
                    FontModel = new RomDataValues("arm9.bin", 0xC7FFC, 0x8284),
                    FontWidths = new RomDataValues("arm9.bin", 0xC02AC, 480),
                    FontOffsets = new RomDataValues("arm9.bin", 0xC048C, 480),
                    FontCharData = new RomDataValues("arm9.bin", 0xC0B6C, 0x4000),
                    TerrianSfx = new RomDataValues("overlay9_2", 0x1DA68, 144),
                    BeamSfx = new RomDataValues("overlay9_2", 0x1DAF8, 180),
                    HunterSfx = new RomDataValues("overlay9_2", 0x1DBAC, 272),
                    EnemyDamageSfx = new RomDataValues("arm9.bin", 0xC5DD0, 208),
                    EnemyDeathSfx = new RomDataValues("arm9.bin", 0xC5EA0, 208),
                    PlatformSfx = new RomDataValues("overlay9_15", 0x8284, 360)
                }
            ),
            new (
                Ver.AMHK0,
                new RomData()
                {
                    FontModel = new RomDataValues("arm9.bin", 0xC0D40, 0x8284),
                    FontWidths = new RomDataValues("arm9.bin", 0xBD580, 480),
                    FontOffsets = new RomDataValues("arm9.bin", 0xBD760, 480),
                    FontCharData = new RomDataValues("arm9.bin", 0xB9560, 0x4000),
                    TerrianSfx = new RomDataValues("overlay9_2", 0x1BDBA, 144),
                    BeamSfx = new RomDataValues("overlay9_2", 0x1BE4A, 180),
                    HunterSfx = new RomDataValues("overlay9_2", 0x1BEFE, 272),
                    EnemyDamageSfx = new RomDataValues("arm9.bin", 0xBE4DC, 208),
                    EnemyDeathSfx = new RomDataValues("arm9.bin", 0xBE5AC, 208),
                    PlatformSfx = new RomDataValues("overlay9_15", 0x7CC0, 360)
                }
            ),
            new (
                Ver.NTRJ0,
                new RomData()
                {
                    // todo: values
                    FontModel = new RomDataValues("arm9.bin", 0xED610, 0x8284),
                    FontWidths = new RomDataValues("arm9.bin", 0x1FC07C, 480),
                    FontOffsets = new RomDataValues("arm9.bin", 0x1FC25C, 480),
                    FontCharData = new RomDataValues("arm9.bin", 0x1FC93C, 0x4000)
                }
            )
        ]);
    }
}
