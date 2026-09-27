using System;
using System.IO;
using System.Text.Json;
using MphRead.Mods.Launcher;
namespace MphRead.Mods.Cosmetics
{
    public static class CosmeticPersistence
    {
        private sealed class SaveData
        {
            public int Version { get; set; } = 1;
            public CosmeticLoadout[] Loadouts { get; set; } = new CosmeticLoadout[7];
            public bool[] Pending { get; set; } = new bool[7];
        }
        private static readonly object Gate = new();
        private static SaveData? _data;
        private static string FilePath => Path.Combine(LauncherPrefs.Directory, "cosmetics.json");
        public static event Action? Changed;
        private static SaveData Data => _data ??= Read(FilePath);
        private static SaveData Read(string path)
        {
            SaveData data;
            try { data = new FileInfo(path).Length <= 65536 ? JsonSerializer.Deserialize<SaveData>(File.ReadAllText(path)) ?? new() : new(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { data = new(); }
            if (data.Loadouts == null || data.Loadouts.Length != 7) data.Loadouts = new CosmeticLoadout[7];
            if (data.Pending == null || data.Pending.Length != 7) data.Pending = new bool[7];
            for (int i = 0; i < 7; i++) data.Loadouts[i] = CosmeticCatalog.Resolve((Hunter)i, data.Loadouts[i]);
            return data;
        }
        public static CosmeticLoadout Get(Hunter hunter)
        { lock (Gate) return (int)hunter < 7 ? Data.Loadouts[(int)hunter] : CosmeticLoadout.Default; }
        public static bool IsPending(Hunter hunter)
        { lock (Gate) return (int)hunter < 7 && Data.Pending[(int)hunter]; }
        public static void Equip(Hunter hunter, CosmeticLoadout value)
        {
            if ((int)hunter >= 7) throw new ArgumentOutOfRangeException(nameof(hunter));
            lock (Gate)
            {
                Data.Loadouts[(int)hunter] = CosmeticCatalog.Resolve(hunter, value);
                Data.Pending[(int)hunter] = true;
                Save();
            }
            Changed?.Invoke();
        }
        public static void MarkSynced(Hunter hunter, CosmeticLoadout submitted)
        {
            lock (Gate)
            {
                if ((int)hunter >= 7 || Data.Loadouts[(int)hunter] != submitted) return;
                Data.Pending[(int)hunter] = false; Save();
            }
        }
        public static void MergeRemote(Hunter hunter, CosmeticLoadout remote)
        {
            lock (Gate)
            {
                if ((int)hunter >= 7 || Data.Pending[(int)hunter]) return;
                Data.Loadouts[(int)hunter] = CosmeticCatalog.Resolve(hunter, remote); Save();
            }
            Changed?.Invoke();
        }
        private static void Save()
        {
            Directory.CreateDirectory(LauncherPrefs.Directory);
            string temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(Data));
            File.Move(temp, FilePath, overwrite: true);
        }
        internal static void ResetCache() { lock (Gate) _data = null; }
    }
}
