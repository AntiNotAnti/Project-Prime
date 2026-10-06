using System.Collections.ObjectModel;
using System.Security.Cryptography;

namespace ProjectPrime.Studio.Shell;

public readonly record struct StudioDocumentId(Guid Value)
{
    public static StudioDocumentId New() => new(Guid.NewGuid());
}
public enum StudioDocumentKind { Map, Replay, ReplayClip }
public enum StudioDocumentState { Open, Closing, Closed }
public enum StudioCloseDecision { Save, Discard, Cancel }
public interface IStudioDocumentNotifications { event Action? Changed; }
public interface IStudioRecoverableDocument { string? RecoveryPath { get; } }
public interface IStudioSaveAsDocument { bool RequiresSaveAs { get; } }
public interface IStudioPackageDocument { IReadOnlyList<string> PackageDirectories {get;} }
public interface IStudioDiscardableDocument { Task DiscardChangesAsync(CancellationToken cancellationToken); }

/// <summary>Only the desktop lifecycle is shared; map/replay models remain authoritative in their existing cores.</summary>
public interface IStudioDocument : IAsyncDisposable
{
    StudioDocumentId Id { get; }
    StudioDocumentKind Kind { get; }
    string Title { get; }
    string? Path { get; }
    bool Dirty { get; }
    bool CanSave { get; }
    StudioDocumentState State { get; }
    Task SaveAsync(string? targetPath, CancellationToken cancellationToken);
    Task RecoverAsync(CancellationToken cancellationToken);
    Task CloseAsync(CancellationToken cancellationToken);
}

/// <summary>A source inspection tab, deliberately unable to edit or save game formats.</summary>
public sealed class StudioSourceDocument : IStudioDocument
{
    public StudioDocumentId Id { get; }
    public StudioDocumentKind Kind { get; }
    public string Title => Path is null ? Kind == StudioDocumentKind.Map ? "Map Studio" : "Replay Studio" : System.IO.Path.GetFileName(Path);
    public string? Path { get; }
    public bool Dirty => false;
    public bool CanSave => false;
    public StudioDocumentState State { get; private set; } = StudioDocumentState.Open;
    public long? Length { get; }
    public DateTimeOffset? LastModifiedUtc { get; }
    public string? Sha256 { get; }

    private StudioSourceDocument(StudioDocumentKind kind, string? path, long? length = null, DateTimeOffset? modified = null, string? hash = null, StudioDocumentId? id = null)
        => (Id, Kind, Path, Length, LastModifiedUtc, Sha256) = (id ?? StudioDocumentId.New(), kind, path, length, modified, hash);

    public static StudioSourceDocument Empty(StudioDocumentKind kind, StudioDocumentId? id = null) => new(kind, null, id: id);

    public static async Task<StudioSourceDocument> InspectAsync(StudioDocumentKind kind, string path,
        IProgress<double>? progress, CancellationToken cancellationToken, StudioDocumentId? id = null)
    {
        path = System.IO.Path.GetFullPath(path);
        if (!Supports(kind, path)) throw new InvalidDataException("This file extension does not match the requested workspace.");
        FileInfo info = new(path);
        long length = info.Length;
        DateTimeOffset modified = info.LastWriteTimeUtc;
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
        byte[] buffer = new byte[65536];
        long readTotal = 0;
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken)) != 0)
        {
            hash.AppendData(buffer, 0, read);
            readTotal += read;
            progress?.Report(length == 0 ? 1 : (double)readTotal / length);
        }
        cancellationToken.ThrowIfCancellationRequested();
        info.Refresh();
        if (info.Length != length || info.LastWriteTimeUtc != modified.UtcDateTime)
            throw new IOException("The file changed during inspection. Open it again after the writer finishes.");
        progress?.Report(1);
        return new(kind, path, length, modified, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant(), id);
    }
    public static bool Supports(StudioDocumentKind kind, string path)
    {
        string extension = System.IO.Path.GetExtension(path).ToLowerInvariant();
        return kind switch
        {
            StudioDocumentKind.Map => extension is ".json" or ".ppmap",
            StudioDocumentKind.Replay => extension == ".ppdemo",
            StudioDocumentKind.ReplayClip => extension == ".ppclip",
            _ => false
        };
    }

    public Task SaveAsync(string? targetPath, CancellationToken cancellationToken) => Task.FromException(new NotSupportedException("Source inspection does not modify game formats."));
    public Task RecoverAsync(CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); return Task.CompletedTask; }
    public Task CloseAsync(CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); State = StudioDocumentState.Closed; return Task.CompletedTask; }
    public ValueTask DisposeAsync() { State = StudioDocumentState.Closed; return ValueTask.CompletedTask; }
}

public sealed class StudioDocumentHost : IAsyncDisposable
{
    public ObservableCollection<IStudioDocument> Documents { get; } = [];
    public IStudioDocument? ActiveDocument { get; private set; }
    public event Action? Changed;
    public void Select(IStudioDocument? document)
    {
        if (document is not null && !Documents.Contains(document)) throw new ArgumentException("Document is not hosted.", nameof(document));
        ActiveDocument = document;
        Changed?.Invoke();
    }
    public IStudioDocument? Find(StudioDocumentKind kind, string path) => Documents.FirstOrDefault(doc => doc.Kind == kind &&
        string.Equals(doc.Path, path, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
    public void Add(IStudioDocument document)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Documents.Any(doc => doc.Id == document.Id)) throw new ArgumentException("Document ID is already hosted.", nameof(document));
        Documents.Add(document);
        if (document is IStudioDocumentNotifications notifications) notifications.Changed += OnDocumentChanged;
        Select(document);
    }
    private void OnDocumentChanged() => Changed?.Invoke();
    public async Task<bool> RequestCloseAsync(IStudioDocument document, Func<IStudioDocument, Task<StudioCloseDecision>> decide,
        Func<IStudioDocument, Task<string?>> saveAs, CancellationToken cancellationToken = default)
    {
        if (!Documents.Contains(document)) return true;
        StudioCloseDecision decision=await PrepareCloseAsync(document, decide, saveAs, cancellationToken);
        if (decision==StudioCloseDecision.Cancel)return false;
        if (decision==StudioCloseDecision.Discard && document is IStudioDiscardableDocument discardable)await discardable.DiscardChangesAsync(cancellationToken);
        await document.CloseAsync(cancellationToken);
        await document.DisposeAsync();
        if (document is IStudioDocumentNotifications notifications) notifications.Changed -= OnDocumentChanged;
        Documents.Remove(document);
        if (ReferenceEquals(ActiveDocument, document)) ActiveDocument = Documents.LastOrDefault();
        Changed?.Invoke();
        return true;
    }
    public async Task<bool> RequestCloseAllAsync(Func<IStudioDocument, Task<StudioCloseDecision>> decide,
        Func<IStudioDocument, Task<string?>> saveAs, CancellationToken cancellationToken = default)
    {
        IStudioDocument[] documents = Documents.ToArray();
        // Preflight every prompt before releasing documents or discarding recovery drafts.
        var decisions=new List<(IStudioDocument Document,StudioCloseDecision Decision)>();
        foreach (IStudioDocument document in documents)
        { var decision=await PrepareCloseAsync(document,decide,saveAs,cancellationToken);if(decision==StudioCloseDecision.Cancel)return false;decisions.Add((document,decision)); }
        foreach(var item in decisions.Where(item=>item.Decision==StudioCloseDecision.Discard))
            if(item.Document is IStudioDiscardableDocument discardable)await discardable.DiscardChangesAsync(cancellationToken);
        foreach (IStudioDocument document in documents)
        {
            await document.CloseAsync(cancellationToken);
            await document.DisposeAsync();
            if (document is IStudioDocumentNotifications notifications) notifications.Changed -= OnDocumentChanged;
            Documents.Remove(document);
        }
        ActiveDocument = null;
        Changed?.Invoke();
        return true;
    }
    private static async Task<StudioCloseDecision> PrepareCloseAsync(IStudioDocument document, Func<IStudioDocument, Task<StudioCloseDecision>> decide,
        Func<IStudioDocument, Task<string?>> saveAs, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!document.Dirty) return StudioCloseDecision.Save;
        StudioCloseDecision decision = await decide(document);
        if (decision == StudioCloseDecision.Cancel) return StudioCloseDecision.Cancel;
        if (decision == StudioCloseDecision.Discard) return StudioCloseDecision.Discard;
        if (!document.CanSave) throw new InvalidOperationException("This document cannot save its unsaved changes.");
        string? targetPath = document is IStudioSaveAsDocument { RequiresSaveAs:true } ? await saveAs(document) : document.Path ?? await saveAs(document);
        if (targetPath is null) return StudioCloseDecision.Cancel;
        await document.SaveAsync(targetPath, cancellationToken);
        if (document.Dirty) throw new IOException("Save completed without clearing the document's unsaved state.");
        return StudioCloseDecision.Save;
    }
    private bool _disposed;
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        List<Exception> failures=[];
        foreach (IStudioDocument document in Documents.ToArray())
        {
            if (document is IStudioDocumentNotifications notifications) notifications.Changed -= OnDocumentChanged;
            try { await document.DisposeAsync(); }
            catch(Exception error) { failures.Add(error); }
        }
        Documents.Clear();
        ActiveDocument = null;
        if(failures.Count>0)throw new AggregateException("Studio documents failed to dispose cleanly.",failures);
    }
}
