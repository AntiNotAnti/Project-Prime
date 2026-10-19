using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using MphRead;
using MphRead.Formats;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.RmlUi.Core;
using MphRead.Mods.Launcher.RmlUi.Render;
using MphRead.Mods.Network;
using MphRead.Mods.Replay;

internal static class ReplayFileAcceptance
{
    internal static void Run()
    {
        string directory = Path.Combine(Path.GetTempPath(), "prime-rmlui-replay-copy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string source = Path.Combine(directory, "synthetic-source.ppdemo");
            byte[] bytes = Enumerable.Range(0, 131073).Select(i => (byte)(i % 251)).ToArray();
            File.WriteAllBytes(source, bytes);
            DateTime sourceStamp = File.GetLastWriteTimeUtc(source);
            string destination = Path.Combine(directory, "copied.ppdemo");
            RmlUiLiveMenuServices.CopyReplay(source, destination, CancellationToken.None, validate: false);
            Check(File.ReadAllBytes(destination).SequenceEqual(bytes)
                && File.ReadAllBytes(source).SequenceEqual(bytes)
                && File.GetLastWriteTimeUtc(source) == sourceStamp
                && !File.Exists(destination + ".importing"),
                "copy publishes exact bytes at final name, preserves source and removes owned staging file");

            byte[] existing = { 17, 31, 43, 59 };
            string occupied = Path.Combine(directory, "occupied.ppdemo");
            File.WriteAllBytes(occupied, existing);
            Exception? occupiedFailure = Failure(() => RmlUiLiveMenuServices.CopyReplay(source, occupied,
                CancellationToken.None, validate: false));
            Check(occupiedFailure != null && File.ReadAllBytes(occupied).SequenceEqual(existing)
                && !File.Exists(occupied + ".importing"),
                "copy refuses an occupied final destination without overwriting or leaving staging files");

            string foreignDestination = Path.Combine(directory, "foreign-staging.ppdemo");
            string foreignStaging = foreignDestination + ".importing";
            File.WriteAllBytes(foreignStaging, existing);
            Exception? foreignFailure = Failure(() => RmlUiLiveMenuServices.CopyReplay(source, foreignDestination,
                CancellationToken.None, validate: false));
            Check(foreignFailure != null && !File.Exists(foreignDestination)
                && File.ReadAllBytes(foreignStaging).SequenceEqual(existing),
                "failed staging acquisition never deletes a preexisting importing file");

            using (var cancelled = new CancellationTokenSource())
            {
                cancelled.Cancel();
                Exception? cancelledFailure = Failure(() => RmlUiLiveMenuServices.CopyReplay(source,
                    foreignDestination, cancelled.Token, validate: false));
                Check(cancelledFailure is OperationCanceledException
                    && !File.Exists(foreignDestination) && File.ReadAllBytes(foreignStaging).SequenceEqual(existing),
                    "pre-cancelled copy leaves foreign staging bytes and final destination untouched");
            }

            // A sparse local fixture gives the main thread time to cancel after this
            // production operation acquires its own staging file, without sleeps.
            string largeSource = Path.Combine(directory, "synthetic-large.ppdemo");
            using (var sparse = new FileStream(largeSource, FileMode.CreateNew, FileAccess.Write))
                sparse.SetLength(256L * 1024 * 1024);
            string cancelledDestination = Path.Combine(directory, "cancelled.ppdemo");
            using (var cancellation = new CancellationTokenSource())
            {
                Task<Exception?> copying = Task.Run(() => Failure(() => RmlUiLiveMenuServices.CopyReplay(
                    largeSource, cancelledDestination, cancellation.Token, validate: false)));
                bool acquired = SpinWait.SpinUntil(() => File.Exists(cancelledDestination + ".importing")
                    || copying.IsCompleted, TimeSpan.FromSeconds(5));
                bool ownedStagingObserved = File.Exists(cancelledDestination + ".importing");
                bool finalUnpublishedDuringCopy = !File.Exists(cancelledDestination);
                cancellation.Cancel();
                Exception? cancelledFailure = copying.GetAwaiter().GetResult();
                Check(acquired && ownedStagingObserved && finalUnpublishedDuringCopy
                    && cancelledFailure is OperationCanceledException
                    && !File.Exists(cancelledDestination) && !File.Exists(cancelledDestination + ".importing")
                    && new FileInfo(largeSource).Length == 256L * 1024 * 1024
                    && File.ReadAllBytes(foreignStaging).SequenceEqual(existing),
                    "cancellation after staging acquisition cleans only owned temporary data and never publishes final output");
            }

            string malformed = Path.Combine(directory, "malformed.ppdemo");
            Exception? malformedFailure = Failure(() => RmlUiLiveMenuServices.CopyReplay(source, malformed,
                CancellationToken.None, validate: true));
            Check(malformedFailure != null && !File.Exists(malformed)
                && !File.Exists(malformed + ".importing") && File.ReadAllBytes(source).SequenceEqual(bytes)
                && File.GetLastWriteTimeUtc(source) == sourceStamp,
                "validation rejects malformed replay bytes without publishing or modifying source");

            ConfiguredReplayReaders(Path.Combine(directory, "configured-reader"));
            VirtualLibrary(Path.Combine(directory, "virtual-library"));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static void ConfiguredReplayReaders(string directory)
    {
        Directory.CreateDirectory(directory);
        string oldRoot=GameFiles.Root, oldCwd=Directory.GetCurrentDirectory(), oldExport=Paths.Export;
        string game=Path.Combine(directory,"synthetic-game-root");Directory.CreateDirectory(game);
        string paths=Path.Combine(directory,"paths.txt");
        string first=Path.Combine(directory,"first-export"), second=Path.Combine(directory,"second-export");
        void Configure(string export)=>File.WriteAllText(paths,"0.19.0.0\nAMHE1="+game+"\nExport="+export+"\n");
        var gate=typeof(RmlUiLiveMenuServices).GetField("ReplayMutationGate",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!;
        var readers=typeof(GameFiles).GetField("_configuredReaders",BindingFlags.Static|BindingFlags.NonPublic)!;
        try
        {
            GameFiles.Root=directory;Directory.SetCurrentDirectory(directory);Configure(first);GameFiles.ApplyPaths();
            using var services=new RmlUiLiveMenuServices();
            Verify(()=>services.ReadTheatreAsync(CancellationToken.None),"library scan");
            var entry=new RmlUiReplayEntry(Path.Combine(second,"_demos","absent.ppdemo"),"synthetic","","",0,0,0,false,false,false,true);
            Verify(()=>services.ValidateReplayAsync(entry,CancellationToken.None),"launch preflight");

            void Verify(Func<Task> start,string label)
            {
                string before=Paths.Export, after=before==first?second:first;
                Task operation;
                bool leased=false, stable=false;
                Monitor.Enter(gate);
                try
                {
                    operation=start();
                    leased=SpinWait.SpinUntil(()=>(int)readers.GetValue(null)!>0,TimeSpan.FromSeconds(2));
                    Configure(after);
                    Check(GameFiles.Problem()==null,"synthetic configured folder remains valid during "+label);
                    stable=Paths.Export==before;
                }
                finally { Monitor.Exit(gate); }
                operation.GetAwaiter().GetResult();
                Check(leased&&stable,label+" holds the configured-files capability while queued behind replay ownership; status refresh cannot republish Paths");
                Check((int)readers.GetValue(null)! == 0&&GameFiles.Problem()==null&&Paths.Export==after,
                    label+" releases its capability so the next status refresh can publish the new configuration");
            }
        }
        finally
        {
            GameFiles.Root=oldRoot;Directory.SetCurrentDirectory(oldCwd);GameFiles.ApplyPaths();Paths.SetPath("Export",oldExport);
        }
    }

    private static void VirtualLibrary(string directory)
    {
        using var configuredFiles = GameFiles.TryUseConfiguredFiles()
            ?? throw new InvalidOperationException("Synthetic library could not retain configured paths.");
        // The production owners use Paths.Export. Change only that process-local
        // key while this synchronous fixture owns the library; restore it exactly.
        var paths = (Dictionary<string, string>)typeof(Paths).GetField("_allPaths",
            BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        bool hadExport = paths.TryGetValue("Export", out string? previousExport);
        paths["Export"] = directory;
        try
        {
            string library = DemoLibrary.Directory;
            Directory.CreateDirectory(library);
            string source = Path.Combine(library, "synthetic-virtual-source.ppdemo");
            var match = new MatchStatePacket { MatchId = 1, AuthorityEpoch = 7,
                RoomKey = "unit-native-virtual", Mode = (byte)GameMode.Battle,
                Flags = MatchStatePacket.FlagInProgress };
            byte[] packet = new byte[1 + MatchStatePacket.Size];
            packet[0] = (byte)PacketType.MatchState; match.Write(packet.AsSpan(1));
            using (var writer = new ReplayWriterV3(source, new ReplayMetadata
            {
                RoomKey = match.RoomKey, Mode = GameMode.Battle, MapHash = 0,
                Bootstrap = new ReplayBootstrap { Packets = new[] { packet } }
            }))
                foreach (uint frame in new uint[] { 0, 10, 30, 60 }) writer.WriteRecord(frame, packet);

            byte[] sourceBytes = File.ReadAllBytes(source);
            string sourceHash = Convert.ToHexString(SHA256.HashData(sourceBytes));
            DateTime sourceStamp = File.GetLastWriteTimeUtc(source);
            string descriptor = ReplayVirtualClips.Save(source, 10, 40, "Original cut");
            using var services = new RmlUiLiveMenuServices();
            RmlUiReplayEntry Row() => services.ReadTheatreAsync(CancellationToken.None).GetAwaiter().GetResult()
                .Single(row => ReplayPathComparer.Same(row.Path, descriptor));
            RmlUiReplayWorkResult Work(RmlUiReplayWork work, RmlUiReplayEntry row, RmlUiReplayInputs inputs = default)
                => services.WorkReplayAsync(new(work, row, inputs), CancellationToken.None).GetAwaiter().GetResult();
            var entry = Row();
            Check(entry.Clip && entry.Playable && !entry.Recoverable && entry.Title == "Original cut"
                && entry.VirtualWitness is { SourceBytes: > 0 } witness
                && Path.GetFileName(witness.SourcePath) == Path.GetFileName(source)
                && File.ReadAllBytes(witness.SourcePath).SequenceEqual(sourceBytes)
                && witness.DescriptorSha256.Length == 64
                && services.ReadTheatreAsync(CancellationToken.None).GetAwaiter().GetResult()
                    .Any(row => row.Path == source && row.VirtualWitness == null),
                "actual native library merges a real owner-written virtual descriptor with unchanged physical replay rows");

            var index = new RmlUiTheatreIndex(); index.Refresh(new[] { entry }); index.Select(0);
            index.Refresh(new[] { entry with { VirtualWitness = entry.VirtualWitness!.Value with { SourceBytes = sourceBytes.Length + 1 } } });
            Check(!index.SelectedUnchanged(entry), "selection completion compares the immutable virtual source witness");
            Check(services.ValidateReplayAsync(entry, CancellationToken.None).GetAwaiter().GetResult() == "",
                "actual native preflight materializes the existing descriptor owner on the storage worker");
            var prepared = RmlUiVirtualReplayLibrary.Resolve(entry, library, CancellationToken.None, out var open);
            Check(prepared is { } ready && open == ReplayOpenResult.Success
                && ReplayPathComparer.Same(ReplayVirtualClips.LogicalPath(ready.PlaybackPath), descriptor)
                && RmlUiVirtualReplayLibrary.PreparedUnchanged(ready, entry, library)
                && ReplayLaunchValidator.Probe(ready.PlaybackPath).Ok,
                "resolved playback cache retains the original descriptor identity and shared launch preflight");
            string cache = prepared!.Value.PlaybackPath;
            ReplayAnnotations.AddBookmark(cache, 5, "Descriptor-owned moment");
            Check(ReplayAnnotations.Bookmarks(descriptor).Count == 1
                && File.Exists(descriptor + ReplayAnnotations.Extension)
                && !File.Exists(cache + ReplayAnnotations.Extension),
                "playback cache annotations bind to the logical descriptor through the existing owner");

            Check(services.ToggleReplayFavorite(entry) && ReplayVirtualClips.IsFavorite(descriptor)
                && !File.Exists(source + ".favorite"), "native favorite writes only the virtual descriptor marker");
            RmlUiReplayWorkResult renamed = Work(RmlUiReplayWork.Rename, entry, new(Name: "Renamed cut"));
            Check(renamed.Success && ReplayVirtualClips.TryLoad(descriptor, out var renamedDocument)
                && renamedDocument?.Name == "Renamed cut" && !File.Exists(descriptor + ".name")
                && !RmlUiVirtualReplayLibrary.Exact(entry, library, requireSource: true)
                && !RmlUiVirtualReplayLibrary.PreparedUnchanged(prepared.Value, entry, library),
                "native rename uses the existing virtual serializer and invalidates the old selection and prepared launch");
            entry = Row();
            RmlUiReplayWorkResult organized = Work(RmlUiReplayWork.Organize, entry,
                new(Tags: "practice, route", Collections: "cuts"));
            Check(organized.Success && ReplayAnnotations.Tags(descriptor).Contains("practice")
                && ReplayAnnotations.Collections(descriptor).Contains("cuts")
                && !File.Exists(source + ReplayAnnotations.Extension),
                "native virtual organization preserves the original physical source and its sidecars");
            RmlUiReplayWorkResult exported = Work(RmlUiReplayWork.Export, entry);
            RmlUiReplayWorkResult recovered = Work(RmlUiReplayWork.Recover, entry);
            Check(!exported.Success && exported.Status.Contains("NOT AVAILABLE")
                && !recovered.Success && recovered.Status.Contains("REFERENCES")
                && !Directory.Exists(Path.Combine(library, "exports")),
                "virtual export and recovery return honest unsupported statuses without copying descriptor JSON as replay bytes");

            byte[] descriptorBytes = File.ReadAllBytes(descriptor);
            DateTime descriptorStamp = File.GetLastWriteTimeUtc(descriptor);
            string reviewedDescriptor = SharedRecordingRevision(descriptor);
            Check(SharedRecordingRevision(source) == sourceBytes.Length + ":" + sourceStamp.Ticks,
                "shared native annotation/camera recording witness preserves physical replay stamp behavior");
            Check(ReplayVirtualClips.Rename(descriptor, "Changed cut"), "fixture mutates the owner-written descriptor");
            File.SetLastWriteTimeUtc(descriptor, descriptorStamp);
            Check(new FileInfo(descriptor).Length == entry.Bytes
                && !RmlUiVirtualReplayLibrary.Exact(entry, library, requireSource: true),
                "descriptor SHA rejects changed JSON even when length and timestamp are restored");
            File.WriteAllBytes(descriptor, descriptorBytes); File.SetLastWriteTimeUtc(descriptor, descriptorStamp);

            var changedRange = JsonNode.Parse(File.ReadAllText(descriptor))!;
            changedRange["StartFrame"] = 20; changedRange["EndFrame"] = 50;
            File.WriteAllText(descriptor, changedRange.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            File.SetLastWriteTimeUtc(descriptor, descriptorStamp);
            Check(new FileInfo(descriptor).Length == descriptorBytes.Length
                && File.GetLastWriteTimeUtc(descriptor) == descriptorStamp
                && SharedRecordingRevision(descriptor) != reviewedDescriptor,
                "actual shared annotation/camera witness rejects virtual range ABA with identical descriptor length and timestamp");
            var changedRangeEntry = Row();
            Check(services.ValidateReplayAsync(changedRangeEntry, CancellationToken.None).GetAwaiter().GetResult() == "",
                "a refreshed changed-range descriptor can pass native preflight with its own new witness");
            using (var changedReader = DemoReader.Open(cache))
                Check(changedReader?.DurationFrames == 10,
                    "native cache witness rebuilds the selected range even when descriptor timestamp was preserved");
            File.WriteAllBytes(descriptor, descriptorBytes); File.SetLastWriteTimeUtc(descriptor, descriptorStamp);
            Check(SharedRecordingRevision(descriptor) == reviewedDescriptor,
                "shared descriptor witness reflects exact restored bytes without changing the virtual serializer");
            string oversizedDescriptor = Path.Combine(library, "oversized-witness.ppclip");
            File.WriteAllBytes(oversizedDescriptor, new byte[64 * 1024 + 1]);
            Check(Failure(() => SharedRecordingRevision(oversizedDescriptor)) is IOException
                && new FileInfo(oversizedDescriptor).Length == 64 * 1024 + 1,
                "shared virtual descriptor hashing is bounded and refuses oversized input without modifying it");
            File.Delete(oversizedDescriptor);
            string witnessLink = Path.Combine(library, "linked-witness.ppclip");
            File.CreateSymbolicLink(witnessLink, descriptor);
            Check(Failure(() => SharedRecordingRevision(witnessLink)) is IOException
                && File.ReadAllBytes(descriptor).SequenceEqual(descriptorBytes),
                "shared recording witness refuses descriptor symbolic links and preserves target bytes");
            File.Delete(witnessLink);
            Check(services.ValidateReplayAsync(entry, CancellationToken.None).GetAwaiter().GetResult() == "",
                "restoring the original descriptor reuses the owner to restore its original range");

            File.WriteAllBytes(source, sourceBytes.Concat(new byte[] { 61 }).ToArray());
            Check(!RmlUiVirtualReplayLibrary.Exact(entry, library, requireSource: true)
                && services.ValidateReplayAsync(entry, CancellationToken.None).GetAwaiter().GetResult().Length > 0
                && !Work(RmlUiReplayWork.Rename, entry, new(Name: "Stale request")).Success,
                "source witness fences stale native preflight and queued file mutations");
            File.WriteAllBytes(source, sourceBytes); File.SetLastWriteTimeUtc(source, sourceStamp);
            entry = Row();

            string outside = Path.Combine(directory, "outside.ppdemo");
            File.WriteAllBytes(outside, sourceBytes);
            var json = JsonNode.Parse(File.ReadAllText(descriptor))!;
            json["SourceReplay"] = outside;
            File.WriteAllText(descriptor, json.ToJsonString());
            var outsideEntry = Row();
            Check(!outsideEntry.Playable && !RmlUiVirtualReplayLibrary.Exact(outsideEntry, library, requireSource: true)
                && !Work(RmlUiReplayWork.Delete, outsideEntry).Success && File.Exists(outside),
                "descriptor pointers outside the canonical local library cannot launch or mutate source files");
            File.WriteAllBytes(descriptor, descriptorBytes); File.SetLastWriteTimeUtc(descriptor, descriptorStamp);
            entry = Row();

            string heldExport = directory + "-held", redirectedExport = directory + "-redirected";
            string redirectedLibrary = Path.Combine(redirectedExport, "_demos");
            Directory.CreateDirectory(redirectedLibrary);
            string redirectedSource = Path.Combine(redirectedLibrary, Path.GetFileName(source));
            string redirectedDescriptor = Path.Combine(redirectedLibrary, Path.GetFileName(descriptor));
            File.WriteAllBytes(redirectedSource, sourceBytes); File.SetLastWriteTimeUtc(redirectedSource, sourceStamp);
            File.WriteAllBytes(redirectedDescriptor, descriptorBytes); File.SetLastWriteTimeUtc(redirectedDescriptor, descriptorStamp);
            Directory.Move(directory, heldExport); Directory.CreateSymbolicLink(directory, redirectedExport);
            try
            {
                Check(new FileInfo(descriptor).Length == entry.Bytes
                    && File.GetLastWriteTimeUtc(descriptor).Ticks == entry.LastWriteTicks
                    && !RmlUiVirtualReplayLibrary.Exact(entry, library, requireSource: true)
                    && services.ValidateReplayAsync(entry, CancellationToken.None).GetAwaiter().GetResult().Length > 0
                    && !Work(RmlUiReplayWork.Delete, entry).Success
                    && File.ReadAllBytes(redirectedSource).SequenceEqual(sourceBytes)
                    && File.ReadAllBytes(redirectedDescriptor).SequenceEqual(descriptorBytes),
                    "configured ancestor redirect cannot borrow matching descriptor/source facts from a different library");
            }
            finally { Directory.Delete(directory); Directory.Move(heldExport, directory); }

            string stableAlias = directory + "-stable-alias";
            Directory.CreateSymbolicLink(stableAlias, directory);
            string aliasClip = "";
            try
            {
                paths["Export"] = stableAlias;
                string aliasedSource = Path.Combine(DemoLibrary.Directory, Path.GetFileName(source));
                aliasClip = ReplayVirtualClips.Save(aliasedSource, 10, 40, "Stable platform alias");
                var aliasRow = services.ReadTheatreAsync(CancellationToken.None).GetAwaiter().GetResult()
                    .Single(row => row.Path == aliasClip);
                Check(aliasRow.Playable && RmlUiVirtualReplayLibrary.Exact(aliasRow, DemoLibrary.Directory, requireSource: true)
                    && File.ReadAllBytes(aliasRow.VirtualWitness!.Value.SourcePath).SequenceEqual(sourceBytes),
                    "stable preexisting directory aliases resolve at the library boundary without rejecting platform temporary paths");
            }
            finally
            {
                if (aliasClip.Length > 0) File.Delete(aliasClip);
                paths["Export"] = directory;
                Directory.Delete(stableAlias);
            }

            string nestedRoot = directory + "-nested-real", nestedOther = directory + "-nested-other";
            string nestedHop = directory + "-nested-hop", nestedExport = directory + "-nested-export";
            string nestedLibrary = Path.Combine(nestedRoot, "export", "_demos");
            Directory.CreateDirectory(nestedLibrary);
            string nestedSource = Path.Combine(nestedLibrary, Path.GetFileName(source));
            File.WriteAllBytes(nestedSource, sourceBytes); File.SetLastWriteTimeUtc(nestedSource, sourceStamp);
            Directory.CreateSymbolicLink(nestedHop, nestedRoot);
            Directory.CreateSymbolicLink(nestedExport, Path.Combine(nestedHop, "export"));
            try
            {
                paths["Export"] = nestedExport;
                string logicalSource = Path.Combine(DemoLibrary.Directory, Path.GetFileName(source));
                string nestedDescriptor = ReplayVirtualClips.Save(logicalSource, 10, 40, "Nested stable alias");
                var nestedRow = services.ReadTheatreAsync(CancellationToken.None).GetAwaiter().GetResult()
                    .Single(row => row.Path == nestedDescriptor);
                Check(nestedRow.Playable && RmlUiVirtualReplayLibrary.Exact(nestedRow, DemoLibrary.Directory, requireSource: true)
                    && nestedRow.VirtualWitness!.Value.SourcePath.Contains(Path.GetFileName(nestedRoot) + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                    && !nestedRow.VirtualWitness!.Value.SourcePath.Contains(Path.GetFileName(nestedHop), StringComparison.Ordinal),
                    "stable aliases inside a directory link target's own ancestry resolve to their physical source owner");
                string nestedOtherLibrary = Path.Combine(nestedOther, "export", "_demos");
                Directory.CreateDirectory(nestedOtherLibrary);
                string nestedOtherSource = Path.Combine(nestedOtherLibrary, Path.GetFileName(source));
                string nestedOtherDescriptor = Path.Combine(nestedOtherLibrary, Path.GetFileName(nestedDescriptor));
                File.WriteAllBytes(nestedOtherSource, sourceBytes); File.SetLastWriteTimeUtc(nestedOtherSource, sourceStamp);
                File.WriteAllBytes(nestedOtherDescriptor, File.ReadAllBytes(nestedDescriptor));
                File.SetLastWriteTimeUtc(nestedOtherDescriptor, File.GetLastWriteTimeUtc(nestedDescriptor));
                Directory.Delete(nestedHop); Directory.CreateSymbolicLink(nestedHop, nestedOther);
                Check(!RmlUiVirtualReplayLibrary.Exact(nestedRow, DemoLibrary.Directory, requireSource: true)
                    && services.ValidateReplayAsync(nestedRow, CancellationToken.None).GetAwaiter().GetResult().Length > 0
                    && !Work(RmlUiReplayWork.Delete, nestedRow).Success
                    && File.ReadAllBytes(nestedSource).SequenceEqual(sourceBytes)
                    && File.ReadAllBytes(nestedOtherSource).SequenceEqual(sourceBytes)
                    && File.Exists(nestedOtherDescriptor),
                    "retargeting an alias inside an unchanged outer link target cannot borrow matching descriptor/source facts");
            }
            finally
            {
                paths["Export"] = directory;
                Directory.Delete(nestedExport); Directory.Delete(nestedHop);
                Directory.Delete(nestedRoot, recursive: true);
                if (Directory.Exists(nestedOther)) Directory.Delete(nestedOther, recursive: true);
            }

            string descriptorLink = Path.Combine(library, "linked.ppclip");
            File.CreateSymbolicLink(descriptorLink, descriptor);
            Check(!services.ReadTheatreAsync(CancellationToken.None).GetAwaiter().GetResult()
                .Any(row => row.Path == descriptorLink), "native virtual listing skips descriptor symbolic links");
            File.Delete(descriptorLink);
            string alias = Path.Combine(library, "source-alias.ppdemo");
            File.CreateSymbolicLink(alias, outside);
            string aliasDescriptor = ReplayVirtualClips.Save(alias, 10, 40, "Linked source");
            var aliasEntry = services.ReadTheatreAsync(CancellationToken.None).GetAwaiter().GetResult()
                .Single(row => row.Path == aliasDescriptor);
            Check(!aliasEntry.Playable && !RmlUiVirtualReplayLibrary.Exact(aliasEntry, library, requireSource: true)
                && File.ReadAllBytes(outside).SequenceEqual(sourceBytes), "native virtual source symbolic links remain unplayable and preserve their targets");
            File.Delete(aliasDescriptor); File.Delete(alias);

            string favorite = descriptor + ".favorite", outsideFavorite = Path.Combine(directory, "outside.favorite");
            File.Delete(favorite); File.WriteAllText(outsideFavorite, "untouched marker");
            File.CreateSymbolicLink(favorite, outsideFavorite);
            Check(!services.ToggleReplayFavorite(entry) && File.ReadAllText(outsideFavorite) == "untouched marker",
                "favorite operation refuses sidecar symbolic links without writing through to their targets");
            File.Delete(favorite);

            string cacheDirectory = Path.GetDirectoryName(cache)!, heldCache = cacheDirectory + "-held";
            string outsideCache = Path.Combine(directory, "outside-cache"); Directory.CreateDirectory(outsideCache);
            Directory.Move(cacheDirectory, heldCache); Directory.CreateSymbolicLink(cacheDirectory, outsideCache);
            try
            {
                Check(RmlUiVirtualReplayLibrary.Resolve(entry, library, CancellationToken.None, out _) == null
                    && !Work(RmlUiReplayWork.Delete, entry).Success
                    && !Directory.EnumerateFileSystemEntries(outsideCache).Any(),
                    "materialization and deletion refuse a redirected virtual cache directory");
            }
            finally { Directory.Delete(cacheDirectory); Directory.Move(heldCache, cacheDirectory); }

            using (var cancelled = new CancellationTokenSource())
            {
                cancelled.Cancel();
                Check(Failure(() => services.ValidateReplayAsync(entry, cancelled.Token).GetAwaiter().GetResult())
                    is OperationCanceledException, "cancelled native virtual preflight cannot publish a launch");
            }

            string missingSource = source + ".offline"; File.Move(source, missingSource);
            var unavailable = Row();
            Check(!unavailable.Playable && !unavailable.Recoverable
                && Work(RmlUiReplayWork.Rename, unavailable, new(Name: "Unavailable source")).Success,
                "missing-source descriptors remain visible and safely renameable while Watch stays unavailable");
            var deleting = Row();
            Check(Work(RmlUiReplayWork.Delete, deleting).Success && !File.Exists(descriptor)
                && !File.Exists(cache) && !File.Exists(descriptor + ReplayAnnotations.Extension)
                && File.ReadAllBytes(missingSource).SequenceEqual(sourceBytes)
                && File.GetLastWriteTimeUtc(missingSource) == sourceStamp
                && Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(missingSource))) == sourceHash
                && File.ReadAllBytes(outside).SequenceEqual(sourceBytes),
                "native delete removes only the descriptor and owned cache/sidecars while preserving every physical source byte and timestamp");
        }
        finally
        {
            if (hadExport) paths["Export"] = previousExport!;
            else paths.Remove("Export");
        }
    }

    private static Exception? Failure(Action action)
    {
        try { action(); return null; }
        catch (Exception error) { return error; }
    }

    private static string SharedRecordingRevision(string path)
    {
        Type witness = typeof(RmlUiOpenGlSmoke).GetNestedType("ReplayAnnotationWitness", BindingFlags.NonPublic)!;
        MethodInfo revision = witness.GetMethod("RecordingRevision", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!;
        try { return (string)revision.Invoke(null, new object[] { path })!; }
        catch (TargetInvocationException error) when (error.InnerException != null) { throw error.InnerException; }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine("RML REPLAY FILE PASS " + message);
    }
}
