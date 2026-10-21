#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.CustomWeapons;
using MphRead.Mods.CustomWeapons.Packages;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Platform;

namespace MphRead.Mods.Launcher.RmlUi.Pages.Studio;

/// <summary>Developer JSON editor. CPU jobs never touch RmlUi; only Refresh adopts their results.</summary>
internal sealed class NativeWeaponStudioPage : IDisposable
{
    private sealed record Completion(WeaponAuthoringWorkspace? Workspace, string Status, bool ReloadFields,
        string? PlayStore = null, string? PlayHash = null, string? PlayRoom = null);
    private readonly RmlUiHost _host;
    private readonly RmlUiPageManager _pages;
    private readonly ConcurrentQueue<Completion> _done = new();
    private readonly NativeWeaponModelPreview _modelPreview = new();
    private RmlUiDocumentToken _document;
    private CancellationTokenRegistration _retirement;
    private WeaponAuthoringWorkspace? _workspace;
    private Process? _play;
    private WeaponPackageLease? _playLease;
    private bool _disposed, _closed, _busy, _dirty, _license;
    private long _revision;
    private string _status = "Create a weapon or open an exact workspace/package file.";
    private string _info = "CPU preview information appears after validation. No GLB, texture or audio converters are provided.";
    private string _name = "", _author = "", _mechanics = "", _model = "";
    private int _confirmAction = -1;
    private string _confirmDraft = "";
    internal RmlUiDocumentToken Document => _document;
    public bool Closed => _closed || _disposed;
    public bool IsBusy => _busy;
    public bool CanNavigateAway => !_busy && !_dirty && !DraftChanged();

    public NativeWeaponStudioPage(RmlUiHost host, RmlUiPageManager pages)
    { _host = host; _pages = pages; }
    public void Open(string? path = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _document = _pages.OpenPage(new("weapon-studio", "pages/studio/weapon-editor.rml", "weapon_project_path"));
        _retirement = _pages.Lifetime(_document).Register(() => { _disposed = _closed = true; StopPlay(); _modelPreview.Dispose(); });
        _host.SetField(_document, "weapon_project_path", path ?? "");
        _host.SetField(_document, "weapon_export_path", "");
        _host.SetField(_document, "weapon_store_path", Environment.GetEnvironmentVariable("PROJECT_PRIME_WEAPON_STORE")
            is { Length: > 0 } store ? store : Path.Combine(AppPaths.UserDataDirectory, "weapons"));
        _host.SetField(_document, "weapon_test_room", "MP3 PROVING GROUND");
        Begin(() => path == null
            ? Loaded(WeaponAuthoringWorkspace.CreateReference(), "New original reference draft. Apply edits, then save.")
            : Loaded(OpenDocument(path), "Opened exact local weapon document."));
    }
    private static WeaponAuthoringWorkspace OpenDocument(string path)
    {
        path = Absolute(path);
        if (path.EndsWith(".ppweapon", StringComparison.OrdinalIgnoreCase)) return WeaponAuthoringWorkspace.ImportArchive(path);
        if (!path.EndsWith(WeaponAuthoringWorkspace.Extension, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Open a .ppweaponproject workspace or .ppweapon package.");
        return WeaponAuthoringWorkspace.Open(path);
    }
    private static string Absolute(string path)
    {
        if (!Path.IsPathFullyQualified(path)) throw new IOException("An absolute local file or directory path is required.");
        return Path.GetFullPath(path);
    }
    private static Completion Loaded(WeaponAuthoringWorkspace workspace, string status)
        => new(workspace, status, true);
    private bool Foreground => !Closed && _pages.Top == _document && _host.CurrentInputDocument == _document;
    private string Field(string name, int maximum = 4096) => _host.ReadField(_document, name, maximum);
    private bool DraftChanged() => _workspace != null && !Closed && _host.IsAlive(_document)
        && (Field("weapon_name", 400) != _name || Field("weapon_author", 600) != _author
            || Field("weapon_mechanics", 65536) != _mechanics || Field("weapon_model", 65536) != _model);
    private string DraftSignature() => Field("weapon_name", 400) + "\0" + Field("weapon_author", 600)
        + "\0" + Field("weapon_mechanics", 65536) + "\0" + Field("weapon_model", 65536);
    private bool ConfirmDiscard(int action)
    {
        if (!_dirty && !DraftChanged()) return true;
        string draft = DraftSignature();
        if (_confirmAction == action && _confirmDraft == draft) { _confirmAction = -1; return true; }
        _confirmAction = action; _confirmDraft = draft;
        _status = "Unsaved changes. Save first, or repeat this action to discard the draft.";
        return false;
    }
    public bool Handle(in RmlUiIntent intent)
    {
        if (!Foreground || intent.Kind != RmlUiIntentKind.StudioAction || intent.Document != _document
            || !_pages.Accept(intent) || intent.Argument is < 224 or > 234) return false;
        if (_busy) return true;
        try
        {
            switch (intent.Argument)
            {
                case 224:
                    if (ConfirmDiscard(224)) Begin(() => Loaded(WeaponAuthoringWorkspace.CreateReference(), "New original reference draft."));
                    break;
                case 225:
                    if (ConfirmDiscard(225))
                    {
                        string path = Absolute(Field("weapon_project_path"));
                        Begin(() => Loaded(OpenDocument(path), "Opened local weapon document."));
                    }
                    break;
                case 226: Apply(); break;
                case 227:
                    _license = !_license;
                    _workspace?.ClearLicenseAttestation(); _dirty = true;
                    _status = _license ? "License permission confirmed locally. Export records this creator declaration." : "License permission is not confirmed.";
                    break;
                case 228:
                {
                    var workspace = RequireApplied(); string path = Absolute(Field("weapon_project_path"));
                    Begin(() => { workspace.Save(path); return Loaded(workspace, "Workspace saved atomically."); });
                    break;
                }
                case 229:
                {
                    var workspace = RequireApplied(); string attestor = Field("weapon_attestor", 600);
                    string path = Absolute(Field("weapon_export_path")); bool permission = _license;
                    if (!path.EndsWith(".ppweapon", StringComparison.OrdinalIgnoreCase)) throw new IOException("Export to a new .ppweapon file.");
                    Begin(() => { workspace.AttestLicense(attestor, permission); workspace.ExportArchive(path);
                        return new(workspace, "Exported new deterministic .ppweapon package. Save the workspace to retain its declaration.", false); });
                    break;
                }
                case 230:
                {
                    var workspace = RequireApplied(); string store = Absolute(Field("weapon_store_path"));
                    Begin(() => { var package = workspace.Install(store); return new(workspace,
                        "Installed exact revision " + package.PackageHash + ". Availability does not grant inventory.", false); });
                    break;
                }
                case 231:
                {
                    if (_play != null) throw new InvalidOperationException("Stop the owned pickup test before starting another.");
                    var workspace = RequireApplied(); string store = Absolute(Field("weapon_store_path")); string room = Field("weapon_test_room", 256);
                    if (!StockRiftlinePickups.Supports(room)) throw new InvalidOperationException("Choose an authored stock pickup-test room, such as MP3 PROVING GROUND.");
                    Begin(() => { var package = workspace.Install(store); return new(workspace, "Starting exact offline pickup test.", false,
                        Path.GetFullPath(store), package.PackageHash, room); });
                    break;
                }
                case 232: StopPlay(); _status = "Owned pickup test stopped."; break;
                case 233: Back(); break;
                case 234:
                    if (_workspace != null) _info = Describe(_workspace.Validate());
                    break;
            }
        }
        catch (Exception error) when (Recoverable(error)) { _status = error.Message; }
        Refresh(); return true;
    }
    private WeaponAuthoringWorkspace RequireApplied()
    {
        if (_workspace == null) throw new InvalidOperationException("Create or open a weapon first.");
        if (DraftChanged()) throw new InvalidOperationException("Apply the name, author and JSON edits before this action.");
        return _workspace;
    }
    private void Apply()
    {
        var workspace = _workspace ?? throw new InvalidOperationException("Create or open a weapon first.");
        string name = Field("weapon_name", 400), author = Field("weapon_author", 600);
        byte[] mechanics = Encoding.UTF8.GetBytes(Field("weapon_mechanics", 65536));
        byte[] model = Encoding.UTF8.GetBytes(Field("weapon_model", 65536));
        Begin(() =>
        {
            var json = JsonNode.Parse(workspace.CopyProjectJson())!.AsObject(); json["name"] = name; json["author"] = author;
            byte[] projectBytes = JsonSerializer.SerializeToUtf8Bytes(json);
            var project = WeaponProject.Parse(projectBytes);
            var resources = workspace.ResourcePaths.ToDictionary(path => path, workspace.CopyResource, StringComparer.Ordinal);
            resources[project.MechanicsPath] = mechanics; resources[project.ModelPath] = model;
            _ = WeaponPackageBuilder.Build(project, resources); // Full preflight before committing any field.
            workspace.EditProjectJson(projectBytes); workspace.EditMechanicsJson(mechanics); workspace.SetResource(project.ModelPath, model);
            return Loaded(workspace, "Applied strict mechanics, original rigid model and metadata validation.");
        });
    }
    private void Begin(Func<Completion> work)
    {
        _busy = true; _confirmAction = -1; _status = "Working on the bounded CPU package snapshot…";
        _ = Task.Run(() =>
        {
            try { _done.Enqueue(work()); }
            catch (Exception error) when (Recoverable(error)) { _done.Enqueue(new(null, error.Message, false)); }
        });
        Refresh();
    }
    private static bool Recoverable(Exception error) => error is IOException or UnauthorizedAccessException
        or InvalidOperationException or ArgumentException or FormatException or JsonException or NotSupportedException
        or System.ComponentModel.Win32Exception or System.Security.SecurityException;
    public void Refresh()
    {
        if (Closed) return;
        while (_done.TryDequeue(out var result))
        {
            _busy = false; _status = result.Status;
            if (result.Workspace != null)
            {
                bool changedOwner = !ReferenceEquals(_workspace, result.Workspace);
                _workspace = result.Workspace;
                if (result.ReloadFields)
                {
                    _name = _workspace.Project.Name; _author = _workspace.Project.Author;
                    _mechanics = Encoding.UTF8.GetString(_workspace.CopyMechanicsJson());
                    _model = Encoding.UTF8.GetString(_workspace.CopyResource(_workspace.Project.ModelPath));
                    _host.SetField(_document, "weapon_name", _name); _host.SetField(_document, "weapon_author", _author);
                    _host.SetField(_document, "weapon_mechanics", _mechanics); _host.SetField(_document, "weapon_model", _model);
                    if (_workspace.SavedPath != null) _host.SetField(_document, "weapon_project_path", _workspace.SavedPath);
                    _license = _workspace.CanDistribute;
                    _host.SetField(_document, "weapon_attestor", _workspace.LicenseAttestation?.Attestor ?? _author);
                    _dirty = _workspace.SavedPath == null || !result.Status.StartsWith("Workspace saved", StringComparison.Ordinal)
                        && !result.Status.StartsWith("Opened", StringComparison.Ordinal);
                }
                if (changedOwner) StopPlay();
                _info = Describe(_workspace.Validate());
                _modelPreview.Request(_workspace.CopyResource(_workspace.Project.ModelPath));
                if (result.PlayHash != null)
                {
                    try { StartPlay(result.PlayStore!, result.PlayHash, result.PlayRoom!); }
                    catch (Exception error) when (Recoverable(error)) { _status = error.Message; }
                }
            }
        }
        if (_play is { } process && process.HasExited)
        { _status = "Pickup test exited with code " + process.ExitCode + "."; StopPlay(); }
        if (!Foreground) return;
        var bindings = new Dictionary<string, RmlUiBindingValue>
        {
            ["image:weapon_model_preview"] = RmlUiBindingValue.FromText(_modelPreview.Read()),
            ["weapon_status"] = RmlUiBindingValue.FromText(_status),
            ["weapon_preview_info"] = RmlUiBindingValue.FromText(_info),
            ["weapon_license"] = RmlUiBindingValue.FromText((_license ? "[X] " : "[ ] ") + "I own or have permission to distribute every included asset"),
            ["disabled:weapon_test_stop"] = RmlUiBindingValue.FromBoolean(_play == null),
            ["disabled:weapon_test_play"] = RmlUiBindingValue.FromBoolean(_busy || _play != null || _workspace == null)
        };
        foreach (string id in new[] { "weapon_new", "weapon_open", "weapon_apply", "weapon_save", "weapon_export", "weapon_install", "weapon_license", "weapon_info_refresh",
            "weapon_project_path", "weapon_export_path", "weapon_store_path", "weapon_test_room", "weapon_attestor", "weapon_name", "weapon_author", "weapon_mechanics", "weapon_model" })
            bindings["disabled:" + id] = RmlUiBindingValue.FromBoolean(_busy);
        _pages.Present(_document, ++_revision, bindings);
    }
    private static string Describe(WeaponPackage package)
    {
        var model = CustomRifleModel.Decode(package.CopyResource(package.Project.ModelPath));
        string cues = string.Join(", ", package.Project.AudioPaths.Select(path =>
            Path.GetFileNameWithoutExtension(path) + ": " + WeaponPackage.DecodeWave(package.CopyResource(path)).Length + " frames"));
        var mechanics = package.Definition.Mechanics;
        return $"CPU validated: LOD boxes {model.Lod(0).Length}/{model.Lod(1).Length}; four mono 22050 Hz cues ({cues}).\n"
            + $"Damage {mechanics.BodyDamage}/{mechanics.HeadDamage}; magazine {mechanics.MagazineCapacity}; reserve {mechanics.InitialReserve}/{mechanics.MaximumReserve}; optics {mechanics.OpticMagnificationsMilli.Count}.\n"
            + "Exact archive " + package.PackageHash + ". Static rigid-model picture only; firing/reload/optics use the separate offline pickup test. No GLB/texture/audio converter.";
    }
    private void StartPlay(string store, string hash, string room)
    {
        string directory = AppContext.BaseDirectory;
        string executable = Path.Combine(directory, OperatingSystem.IsWindows() ? "ProjectPrime.exe" : "ProjectPrime");
        string assembly = Path.Combine(directory, "ProjectPrime.dll");
        var start = new ProcessStartInfo { UseShellExecute = false, WorkingDirectory = directory };
        if (File.Exists(executable)) start.FileName = executable;
        else if (File.Exists(assembly))
        {
            string? runtime = Environment.GetEnvironmentVariable("DOTNET_ROOT");
            start.FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")
                ?? (runtime == null ? "dotnet" : Path.Combine(runtime, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"));
            start.ArgumentList.Add(assembly);
        }
        else throw new IOException("The paired Project Prime game executable is unavailable in this installation.");
        foreach (string argument in new[] { "-weaponplay", store, hash, room, "-seconds", "120" }) start.ArgumentList.Add(argument);
        _playLease = new WeaponPackageStore(store).AcquireInstalled(hash);
        try { _play = Process.Start(start) ?? throw new IOException("The pickup test process could not start."); }
        catch { _playLease.Dispose(); _playLease = null; throw; }
        _status = "Offline pickup test started for at most 120 seconds. Collect the authored world weapon; inventory is never granted by Studio.";
    }
    private void StopPlay()
    {
        var process = _play; _play = null;
        try { if (process != null && !process.HasExited) { process.Kill(entireProcessTree: true); process.WaitForExit(2000); } }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        finally { process?.Dispose(); _playLease?.Dispose(); _playLease = null; }
    }
    public bool Back()
    {
        if (!Foreground || _busy) return false;
        if (!ConfirmDiscard(233)) { Refresh(); return true; }
        StopPlay(); _closed = true; return true;
    }
    public void Dispose()
    {
        _retirement.Dispose(); if (_disposed) return;
        _disposed = _closed = true; StopPlay(); _modelPreview.Dispose();
    }
}
#endif
