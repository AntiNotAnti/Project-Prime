using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.Import;

// One owner across views/activities, including a provider that ignores an abort.
// A retired view cannot release the slot while its source is still being read.
internal static class SourceCharacterImportWork
{
    private static int _busy;
    internal static bool IsBusy => Volatile.Read(ref _busy) != 0;
    internal static Task<T> RunExclusive<T>(Func<T> work, CancellationToken token)
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
            throw new InvalidOperationException("The previous source operation is still finishing.");
        return Task.Run(() =>
        {
            try { token.ThrowIfCancellationRequested(); return work(); }
            finally { Volatile.Write(ref _busy, 0); }
        }, CancellationToken.None);
    }
    internal sealed record Outcome(bool Success, bool Cancelled, int Published, string Message);
    internal sealed class Operation
    {
        private readonly CancellationTokenSource _cancel;
        private readonly object _gate = new();
        private bool _finished;
        private string _phase = "Choose your owned source disc";
        private long _bytes;
        internal string Phase => Volatile.Read(ref _phase);
        internal long CopiedBytes => Interlocked.Read(ref _bytes);
        internal Task<Outcome> Completion { get; private set; } = null!;
        internal Operation(CancellationToken owner, CancellationToken activity)
            => _cancel = CancellationTokenSource.CreateLinkedTokenSource(owner, activity);
        internal void Start(SourceDocumentPlatform.Registration platform, string root, string[] ids,
            Func<string, string, CancellationToken, IProgress<ImportPublishStage>, string[], IReadOnlyList<string>> execute)
            => Completion = Run(platform, root, ids, execute);
        internal void Cancel() { lock (_gate) { if (!_finished) _cancel.Cancel(); } }
        private async Task<Outcome> Run(SourceDocumentPlatform.Registration platform, string root, string[] ids,
            Func<string, string, CancellationToken, IProgress<ImportPublishStage>, string[], IReadOnlyList<string>> execute)
        {
            CancellationToken token = _cancel.Token;
            try
            {
                token.ThrowIfCancellationRequested();
                OwnedSourceDocument? document = await platform.Pick(token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (document == null) return new(false, true, 0, "Source selection cancelled.");
                return await Task.Run(async () =>
                {
                    token.ThrowIfCancellationRequested();
                    Volatile.Write(ref _phase, "Copying source to private temporary storage");
                    OwnedSourceSnapshot.RecoverAbandoned(platform.ScratchRoot);
                    using var source = await OwnedSourceSnapshot.AcquireAsync(document.Open, platform.ScratchRoot,
                        token, count => Interlocked.Exchange(ref _bytes, count)).ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    Volatile.Write(ref _phase, "Verifying source and importing appearance bundles");
                    var progress = new ImportProgress(stage => Volatile.Write(ref _phase, stage switch
                    {
                        ImportPublishStage.Checking => "Checking source and bundle",
                        _ => "Importing appearance bundles"
                    }));
                    IReadOnlyList<string> published = execute(source.Path, root, token, progress, ids);
                    // Publication is atomic per bundle. A late cancellation must
                    // not disguise or roll back a successfully returned commit.
                    return new Outcome(true, false, published.Count,
                        $"Imported {published.Count} private appearance bundle(s). These are not playable characters.");
                }, CancellationToken.None).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            { return new(false, true, 0, "Import cancelled. Completed bundles remain installed; use Check to inspect them."); }
            catch (Exception)
            {
                // Provider exceptions can contain document URIs and account
                // names. Keep the visible error bounded and source independent.
                return new(false, token.IsCancellationRequested, 0,
                    token.IsCancellationRequested ? "Import cancelled. Use Check to inspect installed bundles."
                    : "Import could not finish. Use Check to inspect installed bundles, then check source support, storage and document access before retrying.");
            }
            finally
            {
                lock (_gate) { _finished = true; _cancel.Dispose(); }
                Volatile.Write(ref _busy, 0);
            }
        }
    }
    private sealed class ImportProgress(Action<ImportPublishStage> report) : IProgress<ImportPublishStage>
    { public void Report(ImportPublishStage value) => report(value); }

    internal static Operation Start(string root, IReadOnlyCollection<string> selectedIds, CancellationToken owner,
        Func<string, string, CancellationToken, IProgress<ImportPublishStage>, string[], IReadOnlyList<string>>? execute = null,
        bool sameSourceGroup = false)
    {
        root = Path.GetFullPath(root);
        string[] ids = selectedIds.ToArray();
        if (ids.Length == 0 || ids.Length != ids.Distinct(StringComparer.Ordinal).Count()
            || ids.Any(id => !RetroCharacterPreviewExporter.Sources.Any(s => s.Id == id)))
            throw new ArgumentException("Choose supported appearance bundles.", nameof(selectedIds));
        var platform = SourceDocumentPlatform.Capture();
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
            throw new InvalidOperationException("The previous source operation is still finishing.");
        try
        {
            var operation = new Operation(owner, platform.Lifetime);
            operation.Start(platform, root, ids, execute ?? ((path, destination, token, progress, selected)
                => sameSourceGroup
                    ? RetroCharacterPreviewExporter.ExportSourceGroupFromOwnedGame(path, selected, destination, token, progress)
                    : RetroCharacterPreviewExporter.ExportFromOwnedGame(path, destination, token, progress, selected)));
            return operation;
        }
        catch { Volatile.Write(ref _busy, 0); throw; }
    }
}
