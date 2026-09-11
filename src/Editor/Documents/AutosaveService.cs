using System.Text.Json;
using MphRead.Mods.MapGen;

namespace ProjectPrime.Editor.Documents;

public sealed class AutosaveService
{
    private readonly string _directory;
    private readonly int _generations;
    private DateTime _lastSaved = DateTime.MinValue;
    private int _lastRevision = -1;

    public AutosaveService(string directory, int generations = 5)
    {
        _directory = System.IO.Path.GetFullPath(directory);
        _generations = Math.Clamp(generations, 1, 20);
    }

    public void Tick(MapDocument document, TimeSpan interval)
    {
        if (!document.IsDirty || document.Revision == _lastRevision
            || DateTime.UtcNow - _lastSaved < interval) return;
        Directory.CreateDirectory(_directory);
        string stem = document.Project.StableId.Replace('.', '-');
        string path = System.IO.Path.Combine(_directory,
            $"{stem}-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}.autosave.json");
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(document.Project,
            MapJsonContext.Default.MapProject);
        string temporary = path + ".tmp";
        File.WriteAllBytes(temporary, bytes);
        File.Move(temporary, path, overwrite: true);
        foreach (FileInfo old in new DirectoryInfo(_directory).EnumerateFiles(stem + "-*.autosave.json")
            .OrderByDescending(file => file.Name, StringComparer.Ordinal).Skip(_generations)) old.Delete();
        _lastRevision = document.Revision;
        _lastSaved = DateTime.UtcNow;
    }

    public string? Latest(string stableId)
    {
        if (!Directory.Exists(_directory)) return null;
        string stem = stableId.Replace('.', '-');
        return new DirectoryInfo(_directory).EnumerateFiles(stem + "-*.autosave.json")
            .OrderByDescending(file => file.Name, StringComparer.Ordinal)
            .Select(file => file.FullName).FirstOrDefault();
    }

    public string? FindRecovery(MapDocument document)
    {
        if (document.Path == null) return null;
        string? latest = Latest(document.Project.StableId);
        if (latest == null) return null;
        return File.GetLastWriteTimeUtc(latest) > File.GetLastWriteTimeUtc(document.Path)
            ? latest : null;
    }

    public void Discard(string path)
    {
        string root = _directory.EndsWith(System.IO.Path.DirectorySeparatorChar)
            ? _directory : _directory + System.IO.Path.DirectorySeparatorChar;
        string full = System.IO.Path.GetFullPath(path);
        if (!full.StartsWith(root, StringComparison.Ordinal)
            || !full.EndsWith(".autosave.json", StringComparison.Ordinal))
            throw new InvalidOperationException("Refusing to discard a file outside the autosave directory.");
        File.Delete(full);
    }
}
