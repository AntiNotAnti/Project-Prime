using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace MphRead
{
public static class ClientSettings
{
        private const string _saveFolder = "Savedata";
        private static readonly JsonSerializerOptions _jsonOpt = new JsonSerializerOptions(JsonSerializerDefaults.General)
        {
            WriteIndented = true,
            Converters = { new ByteArrayConverter() }
        };

        internal static string SettingsPath
            => Paths.Combine(_saveFolder, "settings.json");

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
            string path = SettingsPath;
            if (File.Exists(path))
            {
                SerializedSettings? settings = JsonSerializer.Deserialize<SerializedSettings>(File.ReadAllText(path), _jsonOpt);
                if (settings != null)
                {
                    if (settings.Features != null)
                    {
                        FeaturesSettings.Load(settings.Features);
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
            File.WriteAllText(SettingsPath, SerializeSettings(menuSettings));
        }

        /// <summary>
        /// Serialize the complete settings.json payload without changing the
        /// live process or filesystem. Settings backup/export uses the same
        /// schema and converters as the normal commit path.
        /// </summary>
        internal static string SerializeSettings(MenuSettings menuSettings)
        {
            ArgumentNullException.ThrowIfNull(menuSettings);
            var settings = new SerializedSettings
            {
                Features = FeaturesSettings.Commit(),
                MenuSettings = menuSettings
            };
            return JsonSerializer.Serialize(settings, _jsonOpt);
        }

        /// <summary>Validate an exported settings.json without applying it.</summary>
        internal static MenuSettings DeserializeSettings(string json)
        {
            ArgumentNullException.ThrowIfNull(json);
            SerializedSettings? settings = JsonSerializer.Deserialize<SerializedSettings>(
                json, _jsonOpt);
            return settings?.MenuSettings
                ?? throw new InvalidDataException(
                    "The settings archive does not contain MenuSettings.");
        }

        /// <summary>Apply a validated imported settings.json to process state.</summary>
        internal static MenuSettings ApplyImportedSettings(string json)
        {
            ArgumentNullException.ThrowIfNull(json);
            SerializedSettings? settings = JsonSerializer.Deserialize<SerializedSettings>(
                json, _jsonOpt);
            MenuSettings menuSettings = settings?.MenuSettings
                ?? throw new InvalidDataException(
                    "The settings archive does not contain MenuSettings.");
            if (settings?.Features != null)
            {
                FeaturesSettings.Load(settings.Features);
            }
            return menuSettings;
        }

}
}
