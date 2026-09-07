using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using MphRead.Entities;
using MphRead.Formats;

namespace MphRead
{
    public static class GameState
    {
        private static string[] BuildDefaultNicknames()
        {
            string[] names = new string[PlayerEntity.SlotCapacity];
            for (int i = 0; i < names.Length; i++)
            {
                names[i] = $"Player{i + 1}";
            }
            return names;
        }

        public static string[] Nicknames { get; } = BuildDefaultNicknames();

        private const string _saveFolder = "Savedata";
        private static readonly JsonSerializerOptions _jsonOpt = new JsonSerializerOptions(JsonSerializerDefaults.General)
        {
            WriteIndented = true,
            Converters = { new ByteArrayConverter() }
        };

        private static string GetSettingsPath()
        {
            return Paths.Combine(_saveFolder, $"settings.json");
        }

        internal sealed class ByteArrayConverter : JsonConverter<byte[]>
        {
            public override byte[]? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                short[]? sByteArray = JsonSerializer.Deserialize<short[]>(ref reader, options);
                if (sByteArray == null)
                {
                    return null;
                }
                byte[] value = new byte[sByteArray.Length];
                for (int i = 0; i < sByteArray.Length; i++)
                {
                    value[i] = (byte)sByteArray[i];
                }
                return value;
            }

            public override void Write(Utf8JsonWriter writer, byte[] value, JsonSerializerOptions options)
            {
                writer.WriteStartArray();
                foreach (byte val in value)
                {
                    writer.WriteNumberValue(val);
                }
                writer.WriteEndArray();
            }
        }

        private class SerializedSettings
        {
            // Bugfixes and Cheats stay at their code defaults -- there is no
            // UI to change them any more, and an old settings.json is not
            // allowed to override that. Features still persists, but only
            // the subset Commit()/Load() actually write -- see Features.cs.
            public IReadOnlyDictionary<string, string>? Features { get; set; }
            public MenuSettings? MenuSettings { get; set; }
        }

        public static MenuSettings LoadSettings()
        {
            string path = GetSettingsPath();
            if (File.Exists(path))
            {
                SerializedSettings? settings = JsonSerializer.Deserialize<SerializedSettings>(File.ReadAllText(path), _jsonOpt);
                if (settings != null)
                {
                    if (settings.Features != null)
                    {
                        Features.Load(settings.Features);
                    }
                    if (settings.MenuSettings != null)
                    {
                        return settings.MenuSettings;
                    }
                }
            }
            return new MenuSettings();
        }

        public static void CommitSettings(MenuSettings menuSettings)
        {
            if (!Directory.Exists(_saveFolder))
            {
                Directory.CreateDirectory(_saveFolder);
            }
            var settings = new SerializedSettings
            {
                Features = Features.Commit(),
                MenuSettings = menuSettings
            };
            File.WriteAllText(GetSettingsPath(), JsonSerializer.Serialize(settings, _jsonOpt));
        }

        public static void Reset()
        {
            if (!Mods.Network.NetSession.Active)
            {
                for (int i = 0; i < Nicknames.Length; i++) { Nicknames[i] = $"Player{i + 1}"; }
            }
            PlayerEntity.Reset();
            CamSeqEntity.Current = null;
            CameraSequence.Current = null;
        }
    }

}
