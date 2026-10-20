using System.Collections;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MphRead;
using MphRead.Mods.Import;

// Optional isolated-process probe: the original is opened read-only, presented
// as an unknown-length/nonseekable document, and imported by the real default
// worker. Original/derived payloads are never printed or retained as evidence.
internal static class OwnedSourceSnapshotOwnedProbe
{
    internal static int Run(string ownedPath)
    {
        const string id = "darksamus-echoes";
        string temporary = Path.Combine(Path.GetTempPath(), "prime-source-document-owned-" + Guid.NewGuid().ToString("N"));
        string imports = Path.Combine(temporary, "imports"), scratch = Path.Combine(temporary, "scratch");
        string core = typeof(RetroCharacterPreviewExporter).Assembly.Location, tool = typeof(OwnedSourceSnapshotOwnedProbe).Assembly.Location;
        string coreBefore = HashFile(core), toolBefore = HashFile(tool); var cachesBefore = Caches();
        string? sourceBefore = null, sourceAfter = null, generation = null, errorType = null, errorSha = null;
        long originalBytes = 0, copiedBytes = 0; int checks = 0, picks = 0, opens = 0, files = 0, required = 0, version = 0;
        bool cleaned = false, scratchEmpty = false, registeredByProbe = false, restored = false, cachesUnchanged = false;
        bool initiallyRegistered = Registered(), initiallyBusy = SourceCharacterImportWork.IsBusy;
        string stateName = "NotStarted"; NonseekableDocument? source = null;
        SourceDocumentPlatform.Registration? registration = null;
        SourceCharacterImportWork.Operation? operation = null;
        SourceCharacterImportWork.Outcome? outcome = null;
        void Check(bool value, string why) { if (!value) throw new InvalidDataException(why); checks++; }
        try
        {
            // Install retires any previous bridge. Decline to replace one rather
            // than pretending a cancelled Activity registration can be restored.
            Check(!initiallyRegistered && !initiallyBusy, "Run this optional probe in an isolated unregistered process.");
            sourceBefore = HashFile(ownedPath); originalBytes = new FileInfo(ownedPath).Length;
            Check(originalBytes is > 0 and <= OwnedSourceSnapshot.MaximumBytes, "Owned input must fit the existing transport budget.");
            Directory.CreateDirectory(temporary);
            registration = SourceDocumentPlatform.Install(token =>
            {
                token.ThrowIfCancellationRequested(); Interlocked.Increment(ref picks);
                return Task.FromResult<OwnedSourceDocument?>(new OwnedSourceDocument(openToken =>
                {
                    openToken.ThrowIfCancellationRequested(); Interlocked.Increment(ref opens);
                    var input = new FileStream(ownedPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                        128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
                    source = new NonseekableDocument(input);
                    return Task.FromResult<Stream>(source);
                }));
            }, scratch);
            registeredByProbe = true;
            // Deliberately no execute override: this must pass through snapshot
            // acquisition, the default exporter and its actual atomic receipts.
            operation = SourceCharacterImportWork.Start(imports, new[] { id }, CancellationToken.None);
            outcome = operation.Completion.GetAwaiter().GetResult(); copiedBytes = operation.CopiedBytes;
            Check(outcome.Success && !outcome.Cancelled && outcome.Published == 1, "Real document worker must publish one selected complete appearance.");
            Check(picks == 1 && opens == 1 && source is { Disposals: 1, LengthRequests: 0, SeekRequests: 0 }
                && source.BytesRead == originalBytes && copiedBytes == originalBytes,
                "Unknown-length/nonseekable provider must copy every byte and dispose exactly once.");
            Check(!SourceCharacterImportWork.IsBusy, "Default worker releases its process slot after resource retirement.");
            scratchEmpty = Directory.Exists(scratch) && !Directory.EnumerateFileSystemEntries(scratch).Any();
            Check(scratchEmpty, "Complete original snapshot and owner lock must be removed before success is observable.");
            var spec = RetroCharacterPreviewExporter.BundleSpec(id);
            var inspection = LocalImportBundle.Inspect(imports, id, spec); stateName = inspection.State.ToString();
            required = spec.RequiredComponents.Length; files = inspection.Files.Count; version = inspection.Receipt?.Version ?? 0;
            Check(spec.Version == 6 && required == 20 && files == 24 && inspection.State == ImportBundleState.Ready
                && inspection.Receipt is { } receipt && receipt.Id == id && receipt.Version == spec.Version
                && receipt.RecipeHash == spec.RecipeHash && receipt.SourceFingerprint == spec.SourceFingerprint
                && receipt.Components.Length == files && receipt.RequiredComponents.Length == files,
                "Actual receipt must match the complete current normal recipe, exact source identity and immutable component inventory.");
            generation = inspection.Receipt!.Generation;
            string folder = Path.GetDirectoryName(inspection.Files.Single(p => Path.GetFileName(p) == id + ".obj"))!;
            Check(Path.GetFileName(folder) == generation && inspection.Files.All(p => Path.GetDirectoryName(p) == folder)
                && spec.RequiredComponents.All(n => inspection.Files.Contains(Path.Combine(folder, n))),
                "Body/ball/manifest/cannon source parts must share the exact committed generation.");
            Check(RetroCharacterPreviewExporter.InspectParts(id, inspection).All(p => p.State == ImportBundleState.Ready
                && p.Components.All(c => c.State == ImportBundleState.Ready)), "Real verified per-part component states are Ready.");
        }
        catch (Exception e) { errorType = e.GetType().Name; errorSha = Sha(Encoding.UTF8.GetBytes(e.Message)); }
        finally
        {
            // Completion was awaited without a local timeout, so no active copy
            // or importer can still be using this tree when cleanup begins.
            try { registration?.Dispose(); }
            catch (Exception e) { errorType ??= e.GetType().Name; errorSha ??= Sha(Encoding.UTF8.GetBytes(e.Message)); }
            restored = Registered() == initiallyRegistered && (!registeredByProbe || !SourceCharacterImportWork.IsBusy);
            try { if (Directory.Exists(temporary)) Directory.Delete(temporary, true); cleaned = !Directory.Exists(temporary); }
            catch (Exception e) { errorType ??= e.GetType().Name; errorSha ??= Sha(Encoding.UTF8.GetBytes(e.Message)); }
            try { sourceAfter = HashFile(ownedPath); }
            catch (Exception e) { errorType ??= e.GetType().Name; errorSha ??= Sha(Encoding.UTF8.GetBytes(e.Message)); }
            var after = Caches(); cachesUnchanged = cachesBefore.All(p => after[p.Key].SequenceEqual(p.Value));
        }
        string coreAfter = HashFile(core), toolAfter = HashFile(tool);
        bool passed = errorType == null && sourceBefore != null && sourceBefore == sourceAfter && cleaned && restored && scratchEmpty
            && source is { Disposals: 1 } && cachesUnchanged && coreBefore == coreAfter && toolBefore == toolAfter;
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            Schema = 1, Kind = "owned-source-document-import", Passed = passed, Checks = checks,
            MetadataOutputOnly = true, CpuOnly = true, RenderingActivated = false, DefaultImporterUsed = true,
            NonseekableUnknownLengthProvider = true, SelectedId = id, OutcomeSuccess = outcome?.Success ?? false,
            OutcomeCancelled = outcome?.Cancelled ?? false, PublishedBundles = outcome?.Published ?? 0,
            PickerCalls = picks, SourceOpenCalls = opens, SourceDisposals = source?.Disposals ?? 0,
            SourceLengthRequests = source?.LengthRequests ?? 0, SourceSeekRequests = source?.SeekRequests ?? 0,
            SourceBytes = originalBytes, CopiedBytes = copiedBytes, ReadyState = stateName, RecipeVersion = version,
            RequiredRecipeComponents = required, CommittedFiles = files, Generation = generation,
            SourceBeforeSha256 = sourceBefore, SourceAfterSha256 = sourceAfter, SourceUnchanged = sourceBefore != null && sourceBefore == sourceAfter,
            CoreBeforeSha256 = coreBefore, CoreAfterSha256 = coreAfter, ToolBeforeSha256 = toolBefore, ToolAfterSha256 = toolAfter,
            ScratchEmptyBeforeOuterCleanup = scratchEmpty, TemporarySourceAndDerivedPayloadsDeleted = cleaned,
            RetainedSourcePayloadsWritten = !cleaned, RegistrationRestored = restored, CachesUnchanged = cachesUnchanged,
            ErrorType = errorType, ErrorMessageSha256 = errorSha
        }));
        return passed ? 0 : 1;
    }
    private static bool Registered()
    { try { _ = SourceDocumentPlatform.Capture(); return true; } catch (InvalidOperationException) { return false; } }
    private static Dictionary<string, object[]> Caches()
    {
        var result = new Dictionary<string, object[]>();
        foreach (string name in new[] { "_modelCache", "_fhModelCache", "_retiredRoomModels", "_effects", "_particleDefs" })
            result.Add(name, ((IEnumerable)typeof(Read).GetField(name, BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!).Cast<object>().ToArray());
        return result;
    }
    private static string HashFile(string path) { using var input = File.OpenRead(path); return Convert.ToHexStringLower(SHA256.HashData(input)); }
    private static string Sha(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private sealed class NonseekableDocument(FileStream input) : Stream
    {
        internal int Disposals, LengthRequests, SeekRequests; internal long BytesRead;
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length { get { Interlocked.Increment(ref LengthRequests); throw new NotSupportedException("Unknown document size."); } }
        public override long Position { get { Interlocked.Increment(ref SeekRequests); throw new NotSupportedException(); } set { Interlocked.Increment(ref SeekRequests); throw new NotSupportedException(); } }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        { int count = await input.ReadAsync(buffer, token); Interlocked.Add(ref BytesRead, count); return count; }
        public override int Read(byte[] buffer, int offset, int count)
        { int read = input.Read(buffer, offset, count); Interlocked.Add(ref BytesRead, read); return read; }
        public override async ValueTask DisposeAsync()
        { Interlocked.Increment(ref Disposals); await input.DisposeAsync(); GC.SuppressFinalize(this); }
        public override long Seek(long offset, SeekOrigin origin) { Interlocked.Increment(ref SeekRequests); throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException(); public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
