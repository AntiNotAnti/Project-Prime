using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MphRead.Mods;

namespace MphRead.Mods.Input;

/// <summary>
/// A three-state capability answer. Unknown is deliberately different from
/// Unsupported: SDL may expose a connected device while a particular optional
/// query is unavailable on the active runtime.
/// </summary>
public enum ControllerFeatureAvailability
{
    Unknown,
    Unsupported,
    Supported
}

public sealed record ControllerDiagnosticState(
    bool Connected,
    string? Name,
    ControllerFamily Family,
    float? LeftX,
    float? LeftY,
    float? RightX,
    float? RightY,
    float? LeftTrigger,
    float? RightTrigger,
    GamepadButtons PhysicalButtons,
    GamepadButtons EffectiveButtons,
    float? EffectiveMoveX,
    float? EffectiveMoveY,
    float? AimDeltaX,
    float? AimDeltaY,
    float? AimVelocityX,
    float? AimVelocityY);

public sealed record ControllerDiagnosticInputSettings(
    float MoveDeadZone,
    float LookDeadZone,
    float OuterDeadZone,
    float MoveActivateThreshold,
    float MoveReleaseThreshold,
    float TriggerPressThreshold,
    float TriggerReleaseThreshold,
    float HorizontalSensitivity,
    float VerticalSensitivity,
    bool InvertY,
    bool GyroEnabled,
    bool GyroInvertX,
    bool GyroInvertY,
    bool HapticsEnabled,
    IReadOnlyDictionary<string, string> PadBindings);

/// <summary>
/// Privacy-safe controller diagnostics. It contains SDL's immutable device
/// metadata and the current neutral/effective input samples, but never account
/// identifiers, serials, user names, machine names, or filesystem paths.
/// </summary>
public sealed record ControllerDiagnosticReport(
    int SchemaVersion,
    DateTimeOffset CapturedAt,
    ControllerBackend Backend,
    bool BackendAvailable,
    bool Connected,
    string? Name,
    ControllerFamily Family,
    string? Guid,
    ushort? VendorId,
    ushort? ProductId,
    ushort? ProductVersion,
    ControllerFeatureAvailability Gyroscope,
    ControllerFeatureAvailability Accelerometer,
    ControllerFeatureAvailability AnalogTriggers,
    ControllerFeatureAvailability Rumble,
    string? SdlMapping,
    string? SdlRuntimeVersion,
    ControllerDiagnosticState RawState,
    ControllerDiagnosticState EffectiveState,
    ControllerDiagnosticInputSettings InputSettings);

public static class ControllerDiagnosticReportBuilder
{
    public const int SchemaVersion = 1;
    private const int MaximumText = 256;
    private static readonly JsonSerializerOptions Json = CreateJson();

    /// <summary>
    /// Build a report from a snapshot published by the platform owner. The
    /// metadata argument is expected to be retained from the device-open
    /// transition; it is not re-queried while this method runs.
    /// </summary>
    public static ControllerDiagnosticReport Capture(
        ControllerCapabilitySnapshot capabilities,
        in GamepadState raw,
        GamepadButtons effectiveButtons,
        float effectiveMoveX,
        float effectiveMoveY,
        float aimDeltaX,
        float aimDeltaY,
        float aimVelocityX,
        float aimVelocityY,
        DateTimeOffset? capturedAt = null)
    {
        ControllerDiagnosticState rawState = State(raw, raw.Buttons,
            effectiveMoveX: null, effectiveMoveY: null,
            aimDeltaX: null, aimDeltaY: null,
            aimVelocityX: null, aimVelocityY: null);
        ControllerDiagnosticState effectiveState = State(raw, effectiveButtons,
            effectiveMoveX, effectiveMoveY, aimDeltaX, aimDeltaY,
            aimVelocityX, aimVelocityY);
        return new ControllerDiagnosticReport(
            SchemaVersion,
            (capturedAt ?? DateTimeOffset.UtcNow).ToUniversalTime(),
            capabilities.Backend,
            capabilities.IsBackendAvailable,
            capabilities.IsConnected,
            SafeText(capabilities.DeviceName),
            capabilities.Family,
            SafeText(capabilities.Guid),
            capabilities.VendorId,
            capabilities.ProductId,
            capabilities.ProductVersion,
            Availability(capabilities.HasGyroscope),
            Availability(capabilities.HasAccelerometer),
            Availability(capabilities.HasAnalogTriggers),
            Availability(capabilities.HasRumble),
            SafeText(capabilities.SdlMapping),
            SafeText(capabilities.SdlRuntimeVersion),
            rawState,
            effectiveState,
            Settings());
    }

    public static string SerializeJson(ControllerDiagnosticReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (report.SchemaVersion != SchemaVersion)
            throw new ArgumentException("Unsupported controller diagnostic schema.", nameof(report));
        return JsonSerializer.Serialize(report, Json) + "\n";
    }

    /// <summary>
    /// Write JSON and human-readable text to an existing diagnostics folder.
    /// The folder itself must not be a symlink/reparse point; generated files
    /// use CreateNew so an existing report is never silently overwritten.
    /// </summary>
    public static (string JsonPath, string TextPath) WriteToDirectory(
        string directory, ControllerDiagnosticReport report)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(report);
        string root = Path.GetFullPath(directory);
        DirectoryInfo info = new(root);
        if (info.Exists && (info.LinkTarget != null
            || (info.Attributes & FileAttributes.ReparsePoint) != 0))
            throw new IOException("Controller diagnostics directory must not be a symbolic link.");
        Directory.CreateDirectory(root);
        string stamp = report.CapturedAt.ToUniversalTime().ToString(
            "yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        for (int attempt = 0; attempt < 8; attempt++)
        {
            string suffix = attempt == 0 ? "" : $"-{attempt}";
            string jsonPath = Path.Combine(root, $"controller-{stamp}{suffix}.json");
            string textPath = Path.Combine(root, $"controller-{stamp}{suffix}.txt");
            bool jsonCreated = false;
            bool textCreated = false;
            try
            {
                CreateNew(jsonPath, SerializeJson(report));
                jsonCreated = true;
                CreateNew(textPath, SerializeText(report));
                textCreated = true;
                return (jsonPath, textPath);
            }
            catch (IOException) when (File.Exists(jsonPath) || File.Exists(textPath))
            {
                // A collision with an earlier report must never delete that
                // report. Only remove files created by this attempt.
                if (jsonCreated) TryDeleteOwned(jsonPath);
                if (textCreated) TryDeleteOwned(textPath);
            }
        }
        throw new IOException("Could not allocate a unique controller diagnostics filename.");
    }

    public static string SerializeText(ControllerDiagnosticReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var text = new StringBuilder(4096);
        text.AppendLine("Project Prime controller diagnostics v1");
        text.AppendLine($"capturedAt={report.CapturedAt.ToUniversalTime():O}");
        text.AppendLine($"backend={report.Backend}");
        text.AppendLine($"backendAvailable={report.BackendAvailable}");
        text.AppendLine($"connected={report.Connected}");
        text.AppendLine($"name={report.Name ?? "unknown"}");
        text.AppendLine($"family={report.Family}");
        text.AppendLine($"guid={report.Guid ?? "unknown"}");
        text.AppendLine($"vendorId={report.VendorId?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}");
        text.AppendLine($"productId={report.ProductId?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}");
        text.AppendLine($"productVersion={report.ProductVersion?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}");
        text.AppendLine($"gyroscope={report.Gyroscope}");
        text.AppendLine($"accelerometer={report.Accelerometer}");
        text.AppendLine($"analogTriggers={report.AnalogTriggers}");
        text.AppendLine($"rumble={report.Rumble}");
        text.AppendLine($"sdlMapping={report.SdlMapping ?? "unknown"}");
        text.AppendLine($"sdlRuntimeVersion={report.SdlRuntimeVersion ?? "unknown"}");
        AppendState(text, "raw", report.RawState);
        AppendState(text, "effective", report.EffectiveState);
        text.AppendLine("inputSettings:");
        text.AppendLine($"  moveDeadZone={report.InputSettings.MoveDeadZone.ToString("R", CultureInfo.InvariantCulture)}");
        text.AppendLine($"  lookDeadZone={report.InputSettings.LookDeadZone.ToString("R", CultureInfo.InvariantCulture)}");
        text.AppendLine($"  outerDeadZone={report.InputSettings.OuterDeadZone.ToString("R", CultureInfo.InvariantCulture)}");
        text.AppendLine($"  moveActivateThreshold={report.InputSettings.MoveActivateThreshold.ToString("R", CultureInfo.InvariantCulture)}");
        text.AppendLine($"  moveReleaseThreshold={report.InputSettings.MoveReleaseThreshold.ToString("R", CultureInfo.InvariantCulture)}");
        text.AppendLine($"  triggerPressThreshold={report.InputSettings.TriggerPressThreshold.ToString("R", CultureInfo.InvariantCulture)}");
        text.AppendLine($"  triggerReleaseThreshold={report.InputSettings.TriggerReleaseThreshold.ToString("R", CultureInfo.InvariantCulture)}");
        text.AppendLine($"  horizontalSensitivity={report.InputSettings.HorizontalSensitivity.ToString("R", CultureInfo.InvariantCulture)}");
        text.AppendLine($"  verticalSensitivity={report.InputSettings.VerticalSensitivity.ToString("R", CultureInfo.InvariantCulture)}");
        text.AppendLine($"  invertY={report.InputSettings.InvertY}");
        text.AppendLine($"  gyroEnabled={report.InputSettings.GyroEnabled}");
        text.AppendLine($"  gyroInvertX={report.InputSettings.GyroInvertX}");
        text.AppendLine($"  gyroInvertY={report.InputSettings.GyroInvertY}");
        text.AppendLine($"  hapticsEnabled={report.InputSettings.HapticsEnabled}");
        foreach ((string key, string value) in report.InputSettings.PadBindings.OrderBy(item => item.Key,
            StringComparer.Ordinal))
            text.AppendLine($"  binding.{key}={value}");
        return text.ToString();
    }

    private static ControllerDiagnosticState State(in GamepadState state,
        GamepadButtons buttons, float? effectiveMoveX, float? effectiveMoveY,
        float? aimDeltaX, float? aimDeltaY, float? aimVelocityX,
        float? aimVelocityY)
        => new(
            state.Connected,
            SafeText(state.Name),
            state.Family,
            Finite(state.LeftX), Finite(state.LeftY),
            Finite(state.RightX), Finite(state.RightY),
            Finite(state.LeftTrigger), Finite(state.RightTrigger),
            state.Buttons,
            buttons,
            Finite(effectiveMoveX), Finite(effectiveMoveY),
            Finite(aimDeltaX), Finite(aimDeltaY),
            Finite(aimVelocityX), Finite(aimVelocityY));

    private static ControllerDiagnosticInputSettings Settings()
    {
        var bindings = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (PadAction action in PadBindings.Actions)
            bindings[PadBindings.Name(action)] = PadBindings.Get(action).ToString();
        return new ControllerDiagnosticInputSettings(
            FiniteOrDefault(InputSettings.GamepadMoveDeadZone),
            FiniteOrDefault(InputSettings.GamepadLookDeadZone),
            FiniteOrDefault(InputSettings.GamepadOuterDeadZone),
            FiniteOrDefault(InputSettings.GamepadMoveActivateThreshold),
            FiniteOrDefault(InputSettings.GamepadMoveReleaseThreshold),
            FiniteOrDefault(InputSettings.GamepadTriggerPressThreshold),
            FiniteOrDefault(InputSettings.GamepadTriggerReleaseThreshold),
            FiniteOrDefault(InputSettings.GamepadHorizontalSensitivity),
            FiniteOrDefault(InputSettings.GamepadVerticalSensitivity),
            InputSettings.GamepadInvertY,
            InputSettings.GamepadGyroEnabled,
            InputSettings.GamepadGyroInvertX,
            InputSettings.GamepadGyroInvertY,
            InputSettings.GamepadHapticsEnabled,
            bindings);
    }

    private static ControllerFeatureAvailability Availability(bool? value)
        => value switch
        {
            true => ControllerFeatureAvailability.Supported,
            false => ControllerFeatureAvailability.Unsupported,
            _ => ControllerFeatureAvailability.Unknown
        };

    private static float? Finite(float value) => float.IsFinite(value) ? value : null;
    private static float? Finite(float? value)
        => value is float number && float.IsFinite(number) ? number : null;
    private static float FiniteOrDefault(float value)
        => float.IsFinite(value) ? value : 0;

    private static string? SafeText(string? value)
    {
        if (String.IsNullOrWhiteSpace(value)) return null;
        string normalized = value.Trim().Replace('\r', ' ').Replace('\n', ' ');
        if (normalized.Length > MaximumText) normalized = normalized[..MaximumText];
        return normalized.Any(Char.IsControl) ? null : normalized;
    }

    private static void AppendState(StringBuilder text, string prefix,
        ControllerDiagnosticState state)
    {
        text.AppendLine($"{prefix}.connected={state.Connected}");
        text.AppendLine($"{prefix}.name={state.Name ?? "unknown"}");
        text.AppendLine($"{prefix}.family={state.Family}");
        text.AppendLine($"{prefix}.leftStick={Pair(state.LeftX, state.LeftY)}");
        text.AppendLine($"{prefix}.rightStick={Pair(state.RightX, state.RightY)}");
        text.AppendLine($"{prefix}.triggers={Pair(state.LeftTrigger, state.RightTrigger)}");
        text.AppendLine($"{prefix}.physicalButtons={state.PhysicalButtons}");
        text.AppendLine($"{prefix}.effectiveButtons={state.EffectiveButtons}");
        text.AppendLine($"{prefix}.effectiveMove={Pair(state.EffectiveMoveX, state.EffectiveMoveY)}");
        text.AppendLine($"{prefix}.aimDelta={Pair(state.AimDeltaX, state.AimDeltaY)}");
        text.AppendLine($"{prefix}.aimVelocity={Pair(state.AimVelocityX, state.AimVelocityY)}");
    }

    private static string Pair(float? x, float? y)
        => $"{Number(x)},{Number(y)}";

    private static string Number(float? value)
        => value?.ToString("R", CultureInfo.InvariantCulture) ?? "unknown";

    private static void CreateNew(string path, string content)
    {
        using FileStream stream = new(path, FileMode.CreateNew, FileAccess.Write,
            FileShare.Read, 4096, FileOptions.SequentialScan);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(content);
    }

    private static void TryDeleteOwned(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static JsonSerializerOptions CreateJson()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            NumberHandling = JsonNumberHandling.Strict
        };
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        return options;
    }
}
