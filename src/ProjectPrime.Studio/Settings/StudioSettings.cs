using System.Text.Json;
using System.Text.Json.Serialization;
using ProjectPrime.Studio.Shell;

namespace ProjectPrime.Studio.Settings;

public sealed class StudioSettings
{
    public int Version { get; set; } = 1;
    public double WindowWidth { get; set; } = 1280;
    public double WindowHeight { get; set; } = 800;
    public string? GamePathsFile { get; set; }
    public bool ReopenLastSession { get; set; } = true;
    public StudioDockLayout Layout { get; set; } = new();
    public StudioDockLayout MapLayout { get; set; } = new() { LeftWidth=260,RightWidth=340,BottomHeight=100,BottomVisible=false };
    public List<StudioRecentDocument> RecentDocuments { get; set; } = [];
    public Dictionary<string, string> CustomHotkeys { get; set; } = new(StringComparer.Ordinal);
}

public sealed class StudioSession
{
    public int Version { get; set; } = 1;
    public bool CleanExit { get; set; }
    public Guid? SelectedDocument { get; set; }
    public List<StudioDocumentSnapshot> Documents { get; set; } = [];
}

public sealed record StudioDocumentSnapshot(Guid Id, StudioDocumentKind Kind, string? Path, string? RecoveryPath = null, string[]? PackageDirectories = null);

public sealed class StudioSettingsStore(StudioPaths paths)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    public string? LastError { get; private set; }

    public StudioSettings LoadSettings()
    {
        StudioSettings settings = Read<StudioSettings>(paths.SettingsFile) ?? new();
        if (settings.Version != 1) return new();
        settings.WindowWidth = double.IsFinite(settings.WindowWidth) ? Math.Clamp(settings.WindowWidth, 900, 4000) : 1280;
        settings.WindowHeight = double.IsFinite(settings.WindowHeight) ? Math.Clamp(settings.WindowHeight, 600, 3000) : 800;
        settings.Layout ??= new();
        settings.Layout.Normalize();
        settings.MapLayout ??= new(); settings.MapLayout.Normalize();
        settings.RecentDocuments ??= [];
        settings.RecentDocuments = settings.RecentDocuments.Where(item => item is not null && !string.IsNullOrWhiteSpace(item.Path) && Path.IsPathFullyQualified(item.Path)).Take(20).ToList();
        settings.CustomHotkeys ??= new(StringComparer.Ordinal);
        settings.CustomHotkeys = settings.CustomHotkeys.Where(item => Enum.TryParse<StudioCommand>(item.Key,out var command) && Enum.IsDefined(command)
            && item.Value is not null && item.Value.Length <= 80).ToDictionary(item=>item.Key,item=>item.Value,StringComparer.Ordinal);
        return settings;
    }

    public StudioSession? LoadSession()
    {
        StudioSession? session = Read<StudioSession>(paths.SessionFile);
        return session?.Version == 1 && session.Documents is not null ? session : null;
    }
    public bool SaveSettings(StudioSettings settings) => Write(paths.SettingsFile, settings);
    public bool SaveSession(StudioSession session) => Write(paths.SessionFile, session);

    private T? Read<T>(string path)
    {
        try
        {
            if (!File.Exists(path)) return default;
            if (new FileInfo(path).Length > 1024 * 1024) { LastError = "Studio settings exceed the size limit."; return default; }
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { LastError = ex.Message; return default; }
    }
    private bool Write<T>(string path, T value)
    {
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(paths.UserDataDirectory);
            File.WriteAllText(temporary, JsonSerializer.Serialize(value, Options));
            File.Move(temporary, path, true);
            LastError = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { LastError = ex.Message; return false; }
        finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch (IOException) { } }
    }
}
