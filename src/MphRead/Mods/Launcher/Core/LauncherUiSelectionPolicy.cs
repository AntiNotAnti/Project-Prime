using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MphRead.Mods.Launcher.Core;

public enum LauncherUiMode { Auto, RmlUi, Legacy }
public enum LauncherUiFailure { None, InterruptedStartup, InvalidState, Initialization, Runtime }
public readonly record struct LauncherUiDecision(LauncherUiMode Requested, LauncherUiMode Selected,
    bool Explicit, bool NativeBlocked, string? RendererRollback);

/// <summary>Runtime selection and a bounded, atomic startup record containing only component labels.</summary>
public sealed class LauncherUiSelectionPolicy
{
    private const int MaximumStateBytes = 8192;
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private readonly bool _nativeAvailable, _legacyAvailable;
    private readonly bool _persistenceEnabled;
    private readonly HashSet<string> _acceptedRids;
    private readonly string _rid, _build, _path;
    private StartupState _state;
    public bool PersistenceUnavailable { get; private set; }
    public LauncherUiFailure Failure => _state.Failure;
    public bool NativeBlocked => _state.NativeBlocked;

    public LauncherUiSelectionPolicy(bool nativeAvailable, bool legacyAvailable, string rid,
        IEnumerable<string> acceptedRids, string statePath, string build, bool persistenceEnabled = true)
    {
        _nativeAvailable = nativeAvailable; _legacyAvailable = legacyAvailable;
        _persistenceEnabled = persistenceEnabled;
        if (String.IsNullOrWhiteSpace(rid) || rid.Length > 64 || rid.Any(c => !Char.IsAsciiLetterOrDigit(c) && c != '-'))
            throw new ArgumentException("Invalid client runtime identifier.", nameof(rid));
        if (String.IsNullOrWhiteSpace(build) || build.Length > 256) throw new ArgumentException("Invalid client build label.", nameof(build));
        _rid = rid; _build = build; _path = Path.GetFullPath(statePath);
        _acceptedRids = new(acceptedRids, StringComparer.OrdinalIgnoreCase);
        _state = Load();
        if (_state.NativePending)
        {
            _state.NativePending = false; _state.NativeBlocked = true;
            _state.Failure = LauncherUiFailure.InterruptedStartup; Save();
        }
    }

    public static bool HasSelectionOption(IEnumerable<string> arguments)
        => arguments.Any(arg => arg.Equals("-ui", StringComparison.OrdinalIgnoreCase)
            || arg.Equals("--ui", StringComparison.OrdinalIgnoreCase) || arg.StartsWith("-ui=", StringComparison.OrdinalIgnoreCase)
            || arg.StartsWith("--ui=", StringComparison.OrdinalIgnoreCase) || arg.StartsWith("ui=", StringComparison.OrdinalIgnoreCase));

    public LauncherUiDecision Resolve(IReadOnlyList<string> arguments)
    {
        VerifyOwner();
        LauncherUiMode requested = LauncherUiMode.Auto;
        bool explicitSelection = false;
        for (int i = 0; i < arguments.Count; i++)
        {
            string argument = arguments[i]; string? value = null;
            if (argument.Equals("-ui", StringComparison.OrdinalIgnoreCase) || argument.Equals("--ui", StringComparison.OrdinalIgnoreCase))
            {
                if (++i >= arguments.Count) throw new ArgumentException("Supply auto, rmlui or legacy after -ui.");
                value = arguments[i];
            }
            else if (argument.StartsWith("-ui=", StringComparison.OrdinalIgnoreCase)
                || argument.StartsWith("--ui=", StringComparison.OrdinalIgnoreCase) || argument.StartsWith("ui=", StringComparison.OrdinalIgnoreCase))
                value = argument[(argument.IndexOf('=') + 1)..];
            if (value == null) continue;
            LauncherUiMode mode = value.ToLowerInvariant() switch
            { "auto" => LauncherUiMode.Auto, "rmlui" => LauncherUiMode.RmlUi, "legacy" => LauncherUiMode.Legacy,
                _ => throw new ArgumentException("Unknown client UI. Choose auto, rmlui or legacy.") };
            if (explicitSelection && requested != mode) throw new ArgumentException("Conflicting client UI options.");
            requested = mode; explicitSelection = true;
        }
        if (!explicitSelection && arguments.Any(arg => arg.Equals("-rmlui", StringComparison.OrdinalIgnoreCase)
            || arg.Equals("-rmluipoc", StringComparison.OrdinalIgnoreCase) || arg.Equals("-rmluipocshot", StringComparison.OrdinalIgnoreCase)))
        { requested = LauncherUiMode.RmlUi; explicitSelection = true; }
        LauncherUiMode selected = requested;
        if (requested == LauncherUiMode.Auto)
        {
            if (!_legacyAvailable && _nativeAvailable && _state.NativeBlocked)
                throw new InvalidOperationException("Automatic native UI startup is blocked after a previous failure. Install the previous Project Prime version or its transitional package. To attempt one native retry, start with -ui rmlui; a successfully presented frame clears the block.");
            if (!_legacyAvailable && _nativeAvailable && !_acceptedRids.Contains(_rid))
                throw new InvalidOperationException("Automatic native UI startup is not accepted for this runtime identifier. Install its compatibility package, or explicitly start a native trial with -ui rmlui.");
            selected = _nativeAvailable && _acceptedRids.Contains(_rid) && !_state.NativeBlocked
                ? LauncherUiMode.RmlUi : LauncherUiMode.Legacy;
        }
        if (selected == LauncherUiMode.RmlUi && !_nativeAvailable)
            throw new InvalidOperationException("This client build does not include the native RmlUi presentation.");
        if (selected == LauncherUiMode.Legacy && !_legacyAvailable)
            throw new InvalidOperationException("This client build does not include the legacy presentation. Install the transitional package or previous Project Prime version to use -ui legacy; use -ui rmlui for an explicit native retry.");
        string? rollback = requested == LauncherUiMode.Auto && _state.NativeBlocked ? _state.LastRenderer : null;
        return new(requested, selected, explicitSelection, _state.NativeBlocked, rollback);
    }

    public void BeginNativeAttempt()
    {
        VerifyOwner();
        if (!_nativeAvailable) throw new InvalidOperationException("Native presentation is unavailable.");
        if (_state.NativePending) return;
        _state.NativePending = true; Save();
    }

    public void RecordNativeFailure(LauncherUiFailure failure)
    {
        VerifyOwner();
        if (failure is not (LauncherUiFailure.Initialization or LauncherUiFailure.Runtime))
            throw new ArgumentException("Choose a native initialization or runtime failure code.", nameof(failure));
        _state.NativePending = false; _state.NativeBlocked = true; _state.Failure = failure; Save();
    }

    /// <summary>Call only after the window's actual surface present succeeds.</summary>
    public void ObservePresented(LauncherUiMode presentation, string renderer)
    {
        VerifyOwner();
        if (presentation is not (LauncherUiMode.RmlUi or LauncherUiMode.Legacy)) throw new ArgumentOutOfRangeException(nameof(presentation));
        string canonical = CanonicalRenderer(renderer) ?? throw new ArgumentException("Unknown renderer label.", nameof(renderer));
        if (presentation == LauncherUiMode.RmlUi && !_nativeAvailable || presentation == LauncherUiMode.Legacy && !_legacyAvailable)
            throw new InvalidOperationException("The presented client UI is unavailable in this build.");
        bool clearNativeFailure = presentation == LauncherUiMode.RmlUi;
        bool changed = _state.LastUi != presentation || _state.LastRenderer != canonical || _state.NativePending
            || clearNativeFailure && (_state.NativeBlocked || _state.Failure != LauncherUiFailure.None);
        _state.LastUi = presentation; _state.LastRenderer = canonical; _state.NativePending = false;
        if (clearNativeFailure) { _state.NativeBlocked = false; _state.Failure = LauncherUiFailure.None; }
        if (changed) Save();
    }

    public void CompleteCleanShutdown()
    {
        VerifyOwner();
        if (!_state.NativePending) return;
        _state.NativePending = false; Save();
    }

    public static string? CanonicalRenderer(string? renderer) => renderer?.ToLowerInvariant() switch
    { "opengl" => "opengl", "dx12" or "directx12" or "directx 12" => "dx12", "vulkan" => "vulkan", "metal" => "metal", _ => null };

    private StartupState Load()
    {
        if (!File.Exists(_path)) return new();
        try
        {
            var file = new FileInfo(_path);
            if (file.Length > MaximumStateBytes) return InvalidState();
            using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read);
            byte[] buffer = new byte[MaximumStateBytes + 1];
            int count = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
            if (count > MaximumStateBytes) return InvalidState();
            StartupState? state = JsonSerializer.Deserialize<StartupState>(buffer.AsSpan(0, count));
            if (state == null || state.Format != 1 || state.Rid != _rid || !Enum.IsDefined(state.Failure)
                || state.LastUi is not (LauncherUiMode.RmlUi or LauncherUiMode.Legacy) && state.LastUi != null
                || state.LastRenderer != null && CanonicalRenderer(state.LastRenderer) != state.LastRenderer
                || state.Build is { Length: > 256 }) return InvalidState();
            return state;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        { return InvalidState(); }
    }
    private static StartupState InvalidState() => new() { NativeBlocked = true, Failure = LauncherUiFailure.InvalidState };
    private void Save()
    {
        if (!_persistenceEnabled) return;
        _state.Rid = _rid; _state.Build = _build;
        string temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(_state);
            if (bytes.Length > MaximumStateBytes) throw new InvalidDataException("UI startup record exceeds its bound.");
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(bytes); stream.Flush(flushToDisk: true); }
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.Move(temporary, _path, overwrite: true); PersistenceUnavailable = false;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        { PersistenceUnavailable = true; }
        finally { try { File.Delete(temporary); } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { } }
    }
    private void VerifyOwner()
    { if (_owner != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Client UI selection belongs to the window owner thread."); }
    private sealed class StartupState
    {
        public int Format { get; set; } = 1;
        public string Rid { get; set; } = "";
        public string Build { get; set; } = "";
        public LauncherUiMode? LastUi { get; set; }
        public string? LastRenderer { get; set; }
        public bool NativePending { get; set; }
        public bool NativeBlocked { get; set; }
        public LauncherUiFailure Failure { get; set; }
    }
}
