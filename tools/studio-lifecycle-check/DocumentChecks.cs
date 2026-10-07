using System.Security.Cryptography;
using ProjectPrime.Studio.Shell;

internal static partial class Program
{
    private static async Task CheckDocumentLifecycleAsync(string directory)
    {
        await using var host = new StudioDocumentHost();
        var first = new FakeDocument(Path.Combine(directory, "first.json"));
        var second = new FakeDocument(null);
        host.Add(first);
        host.Add(second);
        Check(host.ActiveDocument == second && host.Documents.Count == 2, "adding documents selects latest tab");
        host.Select(first);
        Check(host.ActiveDocument == first && host.Find(StudioDocumentKind.Map, first.Path!) == first,
            "host selection and exact source lookup");
        Check(!await host.RequestCloseAsync(first, _ => Task.FromResult(StudioCloseDecision.Cancel), _ => Task.FromResult<string?>(null))
            && host.Documents.Contains(first) && first.Dirty && first.DisposeCalls == 0,
            "dirty close Cancel preserves draft and resources");
        Check(!await host.RequestCloseAsync(second, _ => Task.FromResult(StudioCloseDecision.Save), _ => Task.FromResult<string?>(null))
            && second.Dirty && second.SaveCalls == 0, "cancelled Save As leaves unsaved tab open");
        Check(!await host.RequestCloseAllAsync(doc => Task.FromResult(doc == first ? StudioCloseDecision.Discard : StudioCloseDecision.Cancel),
            _ => Task.FromResult<string?>(null)) && host.Documents.Count == 2 && first.DisposeCalls == 0 && second.DisposeCalls == 0,
            "Close All preflights prompts before releasing any document");
        string savedPath = Path.Combine(directory, "saved-as.json");
        Check(await host.RequestCloseAsync(second, _ => Task.FromResult(StudioCloseDecision.Save), _ => Task.FromResult<string?>(savedPath))
            && second.SaveCalls == 1 && second.Path == savedPath && second.CloseCalls == 1 && second.DisposeCalls == 1
            && host.ActiveDocument == first && File.ReadAllText(savedPath) == "draft",
            "Save As persists draft before close and selects remaining tab");
        first.FailSave = true;
        await ExpectAsync<IOException>(() => host.RequestCloseAsync(first, _ => Task.FromResult(StudioCloseDecision.Save),
            _ => Task.FromResult<string?>(null)), "failed save propagates");
        Check(host.Documents.Contains(first) && first.Dirty && first.CloseCalls == 0, "failed save retains dirty document");
        first.FailSave = false;
        Check(await host.RequestCloseAsync(first, _ => Task.FromResult(StudioCloseDecision.Save), _ => Task.FromResult<string?>(null))
            && first.SaveCalls == 2 && first.CloseCalls == 1 && first.DisposeCalls == 1 && host.ActiveDocument is null,
            "dirty Save writes before releasing final tab");

        var discard = new FakeDocument(null);
        host.Add(discard);
        Check(await host.RequestCloseAsync(discard, _ => Task.FromResult(StudioCloseDecision.Discard), _ => Task.FromResult<string?>(null))
            && discard.SaveCalls == 0 && discard.DisposeCalls == 1, "Discard closes without writing draft");

        var recents = new StudioRecentDocuments([]);
        for (int index = 0; index < 25; index++) recents.Add(new FakeDocument(Path.Combine(directory, "recent-" + index + ".json")));
        var reopened = new FakeDocument(Path.Combine(directory, "recent-20.json"));
        recents.Add(reopened);
        Check(recents.Items.Count == 20 && recents.Items[0].Path == reopened.Path
            && recents.Items.Count(item => item.Path == reopened.Path) == 1,
            "recents are bounded and reopening promotes one existing entry");
        recents.Add(new FakeDocument(null));
        Check(recents.Items.Count == 20, "unsaved documents do not create recent-file entries");

        string source = Path.Combine(directory, "inspect.ppdemo");
        byte[] bytes = "source bytes for inspection only"u8.ToArray();
        await File.WriteAllBytesAsync(source, bytes);
        await using StudioSourceDocument inspected = await StudioSourceDocument.InspectAsync(StudioDocumentKind.Replay, source, null, CancellationToken.None);
        Check(!inspected.CanSave && !inspected.Dirty && inspected.Length == bytes.Length
            && inspected.Sha256 == Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            "source inspection is readonly and preserves exact byte identity");
        await ExpectAsync<NotSupportedException>(() => inspected.SaveAsync(source, CancellationToken.None), "inspection cannot overwrite a game format");
        Check(File.ReadAllBytes(source).SequenceEqual(bytes), "source bytes remain unchanged");
        await ExpectAsync<FileNotFoundException>(() => StudioSourceDocument.InspectAsync(StudioDocumentKind.Replay,
            Path.Combine(directory, "missing.ppdemo"), null, CancellationToken.None), "missing source fails without a partially opened document");
        await ExpectAsync<InvalidDataException>(() => StudioSourceDocument.InspectAsync(StudioDocumentKind.Map,
            source, null, CancellationToken.None), "workspace rejects wrong extension");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await ExpectAsync<OperationCanceledException>(() => StudioSourceDocument.InspectAsync(StudioDocumentKind.Replay,
            source, null, cancellation.Token), "cancelled source preparation does not publish a tab");

        var owned = new FakeDocument(null);
        host.Add(owned);
        await host.DisposeAsync();
        await host.DisposeAsync();
        Check(owned.DisposeCalls == 1 && host.Documents.Count == 0, "document owner shutdown disposes each document once");
    }

    private static async Task ExpectAsync<TException>(Func<Task> action, string message) where TException : Exception
    {
        try { await action(); }
        catch (TException) { Check(true, message); return; }
        Check(false, message);
    }

    // This fake verifies only shared close/save ownership; it represents no map or replay format.
    private sealed class FakeDocument(string? path) : IStudioDocument
    {
        public StudioDocumentId Id { get; } = StudioDocumentId.New();
        public StudioDocumentKind Kind => StudioDocumentKind.Map;
        public string Title => "Lifecycle draft";
        public string? Path { get; private set; } = path;
        public bool Dirty { get; private set; } = true;
        public bool CanSave => true;
        public StudioDocumentState State { get; private set; } = StudioDocumentState.Open;
        public bool FailSave { get; set; }
        public int SaveCalls { get; private set; }
        public int CloseCalls { get; private set; }
        public int DisposeCalls { get; private set; }
        public async Task SaveAsync(string? targetPath, CancellationToken cancellationToken)
        {
            SaveCalls++;
            cancellationToken.ThrowIfCancellationRequested();
            if (FailSave) throw new IOException("Expected save failure.");
            await File.WriteAllTextAsync(targetPath!, "draft", cancellationToken);
            Path = targetPath;
            Dirty = false;
        }
        public Task RecoverAsync(CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); return Task.CompletedTask; }
        public Task CloseAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CloseCalls++;
            State = StudioDocumentState.Closed;
            return Task.CompletedTask;
        }
        public ValueTask DisposeAsync() { DisposeCalls++; State = StudioDocumentState.Closed; return ValueTask.CompletedTask; }
    }
}
