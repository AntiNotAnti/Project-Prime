using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Sound;

namespace MphRead
{
    public enum TransitionState
    {
        None = 0,
        Start = 1,
        Process = 2,
        End = 3
    }

    public enum EscapeState
    {
        None = 0,
        Event = 1,
        Escape = 2
    }

    public static class GameState
    {
        // Legacy content/campaign selector; multiplayer runtime uses scene.Match.Rules.Mode.
        public static GameMode Mode { get; set; } = GameMode.SinglePlayer;
        public static bool SinglePlayer => Mode == GameMode.SinglePlayer;
        public static bool Multiplayer => Mode != GameMode.SinglePlayer;
        public static bool PausePrevented { get; set; }
        public static bool MenuPause { get; private set; }
        public static bool DialogPause { get; private set; }
        public static TransitionState TransitionState { get; set; } = TransitionState.None;
        public static bool InRoomTransition => TransitionState != TransitionState.None;
        public static EscapeState EscapeState { get; set; } = EscapeState.None;
        public static float EscapeTimer { get; set; } = -1;
        public static bool EscapePaused { get; set; }
        private static string[] BuildDefaultNicknames()
        {
            string[] names = new string[PlayerEntity.SlotCapacity];
            for (int i = 0; i < names.Length; i++)
            {
                names[i] = $"Player{i + 1}";
            }
            return names;
        }

        public static int TransitionRoomId { get; set; } = -1;
        public static bool TransitionAltForm { get; set; }
        public static string[] Nicknames { get; } = BuildDefaultNicknames();
        private static bool _pausingDialog = false;
        private static bool _unpausingDialog = false;

        public static void PauseMenu()
        {
            MenuPause = true;
            Sfx.Instance.StopAllSound();
            Sfx.TimedSfxMute++;
        }

        public static void UnpauseMenu()
        {
            MenuPause = false;
            Sfx.TimedSfxMute--;
        }

        public static void PauseDialog()
        {
            _pausingDialog = true;
        }

        public static void UnpauseDialog()
        {
            _unpausingDialog = true;
        }

        public static void ApplyPause()
        {
            if (CameraSequence.Current?.Flags.TestFlag(CamSeqFlags.BlockInput) == true)
            {
                return;
            }
            if (_pausingDialog)
            {
                DialogPause = true;
            }
            if (_unpausingDialog)
            {
                DialogPause = false;
            }
            _pausingDialog = false;
            _unpausingDialog = false;
        }

        public static void ResetEscapeState(bool updateSounds)
        {
            EscapeState = EscapeState.None;
            EscapeTimer = -1;
            EscapePaused = false;
            if (updateSounds)
            {
                UpdateEventSounds(timer: -1);
            }
        }

        private static bool _playedTimedEventSfx = false;

        private static void UpdateEventSounds(float timer)
        {
            Music.UpdateEventMusic(timer);
            if (timer > 165 / 30f)
            {
                _playedTimedEventSfx = false;
            }
            else if (timer >= 0 && !_playedTimedEventSfx)
            {
                PlayerEntity.Main.PlayTimedSfx(SfxId.PUZZLE_TIMER1_SCR);
                _playedTimedEventSfx = true;
            }
            else if (timer < 0)
            {
                PlayerEntity.Main.StopTimedSfx(SfxId.PUZZLE_TIMER1_SCR);
                _playedTimedEventSfx = false;
            }
        }

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
            TransitionState = TransitionState.None;
            TransitionRoomId = -1;
            TransitionAltForm = false;
            if (!Mods.Network.NetSession.Active)
            {
                for (int i = 0; i < Nicknames.Length; i++) { Nicknames[i] = $"Player{i + 1}"; }
            }
            PlayerEntity.Reset();
            CamSeqEntity.Current = null;
            CameraSequence.Current = null;
            MenuPause = false;
            DialogPause = false;
            _pausingDialog = false;
            _unpausingDialog = false;
            PausePrevented = false;
            EscapeState = EscapeState.None;
            EscapeTimer = -1;
            EscapePaused = false;
        }
    }

}
