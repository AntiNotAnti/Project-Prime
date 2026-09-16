using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace MphRead.Mods.Input;

/// <summary>
/// Small best-effort persistence boundary for learned controller calibration
/// and device-specific controller settings. Gameplay owns calibration mutation;
/// disk I/O happens only during startup and process shutdown, never on the
/// fixed simulation thread.
/// </summary>
internal static class ControllerCalibrationPersistence
{
    private const int SchemaVersion = 2;
    private const string FileName = "controller-calibration.txt";
    private static readonly object Gate = new();
    private static readonly Dictionary<string, string[]> DeviceSettings = new(
        StringComparer.Ordinal);
    private static string[] _baseSettings = Array.Empty<string>();
    private static string? _activeDeviceId;
    private static bool _initialized;

    internal static void Initialize(string directory,
        ControllerStickCalibrationStore store)
    {
        lock (Gate)
        {
            if (_initialized) return;
            _baseSettings = CaptureControllerSettings();
            Load(directory, store);
            LoadDeviceSettings(directory);
            _initialized = true;
        }
    }

    /// <summary>
    /// Swap the controller-owned controls only when the physical controller
    /// identity changes. The outgoing profile is captured before the incoming
    /// one is applied, so deadzones, curves, sensitivity, inversion, gyro and
    /// bindings remain independent without any per-frame allocation.
    /// </summary>
    internal static void Activate(string? deviceId)
    {
        if (String.IsNullOrWhiteSpace(deviceId)) return;
        lock (Gate)
        {
            if (!_initialized || StringComparer.Ordinal.Equals(
                deviceId, _activeDeviceId)) return;
            CaptureActiveSettings();
            _activeDeviceId = deviceId;
            InputSettings.LoadLines(DeviceSettings.TryGetValue(deviceId,
                out string[]? saved) ? saved : _baseSettings);
        }
    }

    internal static void Load(string directory,
        ControllerStickCalibrationStore store)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(store);
        try
        {
            string path = Path.Combine(directory, FileName);
            if (!File.Exists(path)) return;
            string[] lines = File.ReadAllLines(path);
            if (lines.Length == 0 || lines[0] != $"schema={SchemaVersion}") return;
            var profiles = new List<ControllerStickCalibrationProfile>();
            for (int i = 1; i < lines.Length; i++)
            {
                string[] fields = lines[i].Split(',', 6);
                if (fields.Length != 6 || fields[0].Length == 0) continue;
                string deviceId;
                try
                {
                    deviceId = Encoding.UTF8.GetString(
                        Convert.FromBase64String(fields[0]));
                }
                catch (FormatException)
                {
                    continue;
                }
                if (float.TryParse(fields[1], NumberStyles.Float,
                        CultureInfo.InvariantCulture, out float centerX)
                    && float.TryParse(fields[2], NumberStyles.Float,
                        CultureInfo.InvariantCulture, out float centerY)
                    && float.TryParse(fields[3], NumberStyles.Float,
                        CultureInfo.InvariantCulture, out float noise)
                    && int.TryParse(fields[4], NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out int samples)
                    && float.TryParse(fields[5], NumberStyles.Float,
                        CultureInfo.InvariantCulture, out float maximumMagnitude))
                {
                    profiles.Add(new ControllerStickCalibrationProfile(deviceId,
                        centerX, centerY, noise, samples, maximumMagnitude));
                }
            }
            store.ImportProfiles(profiles);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException)
        {
            // Calibration is an optional refinement. Invalid or inaccessible
            // local data must never prevent controller input or game startup.
        }
    }

    internal static void Save(string directory,
        ControllerStickCalibrationStore store)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(store);
        try
        {
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, FileName);
            string temporary = path + ".tmp";
            var lines = new List<string> { $"schema={SchemaVersion}" };
            foreach (ControllerStickCalibrationProfile profile
                in store.ExportProfiles())
            {
                string id = Convert.ToBase64String(
                    Encoding.UTF8.GetBytes(profile.DeviceId));
                lines.Add(String.Join(',', id,
                    profile.CenterX.ToString("R", CultureInfo.InvariantCulture),
                    profile.CenterY.ToString("R", CultureInfo.InvariantCulture),
                    profile.Noise.ToString("R", CultureInfo.InvariantCulture),
                    profile.Samples.ToString(CultureInfo.InvariantCulture),
                    profile.MaximumMagnitude.ToString("R",
                        CultureInfo.InvariantCulture)));
            }
            lock (Gate)
            {
                CaptureActiveSettings();
                foreach ((string deviceId, string[] settings) in DeviceSettings)
                {
                    string id = Convert.ToBase64String(
                        Encoding.UTF8.GetBytes(deviceId));
                    string payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(
                        String.Join('\n', settings)));
                    lines.Add($"settings,{id},{payload}");
                }
            }
            File.WriteAllLines(temporary, lines, new UTF8Encoding(false));
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException)
        {
            // The controls file follows the same best-effort preference policy.
        }
    }

    private static void LoadDeviceSettings(string directory)
    {
        string path = Path.Combine(directory, FileName);
        if (!File.Exists(path)) return;
        try
        {
            string[] lines = File.ReadAllLines(path);
            if (lines.Length == 0 || lines[0] != $"schema={SchemaVersion}") return;
            foreach (string line in lines.Skip(1))
            {
                if (!line.StartsWith("settings,", StringComparison.Ordinal)) continue;
                string[] fields = line.Split(',', 3);
                if (fields.Length != 3) continue;
                try
                {
                    string deviceId = Encoding.UTF8.GetString(
                        Convert.FromBase64String(fields[1]));
                    string payload = Encoding.UTF8.GetString(
                        Convert.FromBase64String(fields[2]));
                    if (String.IsNullOrWhiteSpace(deviceId)
                        || deviceId.Length > 256) continue;
                    string[] settings = payload.Split('\n',
                        StringSplitOptions.RemoveEmptyEntries
                            | StringSplitOptions.TrimEntries);
                    DeviceSettings[deviceId] = settings
                        .Where(IsControllerSetting).ToArray();
                }
                catch (FormatException)
                {
                    // Ignore only the malformed profile; retain valid peers.
                }
            }
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException)
        {
            // Device profiles are optional local preferences. A missing or
            // unreadable file must not block controller input or startup.
        }
    }

    private static void CaptureActiveSettings()
    {
        if (_activeDeviceId is string active)
            DeviceSettings[active] = CaptureControllerSettings();
    }

    private static string[] CaptureControllerSettings()
        => InputSettings.GetSaveLines().Where(IsControllerSetting).ToArray();

    private static bool IsControllerSetting(string line)
        => line.StartsWith("gamepad_", StringComparison.Ordinal)
            || line.StartsWith("controller_preset=", StringComparison.Ordinal)
            || line.StartsWith("pad_", StringComparison.Ordinal);

    internal static void ResetForTests()
    {
        lock (Gate)
        {
            DeviceSettings.Clear();
            _baseSettings = Array.Empty<string>();
            _activeDeviceId = null;
            _initialized = false;
        }
    }
}
