#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Launcher.RmlUi.Core;
using MphRead.Mods.Network;
using MphRead.Mods.Replay;

namespace MphRead.Mods.Launcher.RmlUi.Render;

internal sealed partial class RmlUiLiveMenuServices
{
    private static readonly object ReplayMutationGate = new();

    public Task<RmlUiReplayWorkResult> WorkReplayAsync(RmlUiReplayWorkRequest request, CancellationToken token)
        => ReplayStorageJobs.Run(() =>
        {
            lock (ReplayMutationGate)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    if (!Enum.IsDefined(request.Work)) return Failed("UNKNOWN REPLAY OPERATION");
                    if (request.Work != RmlUiReplayWork.Import
                        && (request.Entry is not { } checkedEntry || !ExactReplay(checkedEntry, allowInterrupted: true)))
                        return Failed("REPLAY CHANGED OR IS OUTSIDE THE LOCAL LIBRARY // REFRESH");
                    if (request.Entry is { VirtualWitness: not null } virtualEntry && request.Work != RmlUiReplayWork.Import)
                        return WorkVirtualReplay(request, virtualEntry, token);
                    string path = request.Entry?.Path ?? "";
                    string status;
                    switch (request.Work)
                    {
                        case RmlUiReplayWork.Validate:
                            ReplayOpenResult validation = ReplayArchive.Validate(path);
                            token.ThrowIfCancellationRequested();
                            DemoLibrary.NoteValidation(path, validation);
                            status = "INTEGRITY // " + validation.ToString().ToUpperInvariant();
                            break;
                        case RmlUiReplayWork.Recover:
                            if (request.Entry?.Recoverable != true) return Failed("RECORDING IS NOT RECOVERABLE");
                            if (!ReplayArchive.Recover(path, out string? recovered, out ReplayOpenResult result, token)
                                || recovered == null) return Failed("RECOVERY FAILED // " + result.ToString().ToUpperInvariant());
                            status = "RECOVERED // ORIGINAL RECORDING PRESERVED";
                            break;
                        case RmlUiReplayWork.Rename:
                            if (String.IsNullOrWhiteSpace(request.Inputs.Name) || request.Inputs.Name.Length > 100)
                                return Failed("USE A NAME BETWEEN 1 AND 100 CHARACTERS");
                            if (new FileInfo(path + ".name").LinkTarget != null) return Failed("REPLAY NAME SIDECAR IS A SYMBOLIC LINK");
                            DemoLibrary.Rename(path, request.Inputs.Name);
                            status = "REPLAY RENAMED";
                            break;
                        case RmlUiReplayWork.Organize:
                            if (path.EndsWith(".part", StringComparison.OrdinalIgnoreCase)) return Failed("RECOVER BEFORE ORGANIZING THIS RECORDING");
                            string tags = request.Inputs.Tags ?? "", collections = request.Inputs.Collections ?? "";
                            if (tags.Length > 1024 || collections.Length > 1024) return Failed("LABEL LIST IS TOO LONG");
                            if (new FileInfo(path + ReplayAnnotations.Extension).LinkTarget != null) return Failed("REPLAY ORGANIZATION SIDECAR IS A SYMBOLIC LINK");
                            ReplayAnnotations.SetOrganization(path,
                                tags.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
                                collections.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
                            status = "REPLAY ORGANIZATION SAVED";
                            break;
                        case RmlUiReplayWork.Delete:
                            if (DemoRecorder.IsRecording) return Failed("STOP RECORDING BEFORE DELETING LIBRARY FILES");
                            DemoLibrary.Delete(path);
                            status = "RECORDING AND LIBRARY SIDECARS DELETED";
                            break;
                        case RmlUiReplayWork.Export:
                            if (path.EndsWith(".part", StringComparison.OrdinalIgnoreCase)) return Failed("RECOVER BEFORE EXPORTING THIS RECORDING");
                            string exports = Path.Combine(DemoLibrary.Directory, "exports");
                            Directory.CreateDirectory(exports);
                            string exported = Path.Combine(exports, "replay_" + Guid.NewGuid().ToString("N") + DemoFile.Extension);
                            CopyReplay(path, exported, token);
                            status = "REPLAY EXPORTED TO LIBRARY / EXPORTS";
                            break;
                        case RmlUiReplayWork.Import:
                            string source = request.Inputs.ImportPath ?? "";
                            if (source.Length > 4096 || !Path.IsPathFullyQualified(source)
                                || !(source.EndsWith(DemoFile.Extension, StringComparison.OrdinalIgnoreCase)
                                    || source.EndsWith(DemoFile.LegacyExtension, StringComparison.OrdinalIgnoreCase)
                                    || RmlUiReplayFileNames.IsPortableBundle(source)))
                                return Failed("ENTER AN ABSOLUTE .PPDEMO, .PPREPLAY.ZIP OR LEGACY REPLAY PATH");
                            var info = new FileInfo(source);
                            if (!info.Exists || info.LinkTarget != null) return Failed("IMPORT SOURCE IS UNAVAILABLE");
                            Directory.CreateDirectory(DemoLibrary.Directory);
                            string imported = Path.Combine(DemoLibrary.Directory, "imported_" + Guid.NewGuid().ToString("N") + DemoFile.Extension);
                            ImportReplayWithSidecars(source, imported, token);
                            status = "REPLAY IMPORTED // SOURCE PRESERVED";
                            break;
                        default: return Failed("REPLAY OPERATION UNAVAILABLE");
                    }
                    token.ThrowIfCancellationRequested();
                    return new RmlUiReplayWorkResult(true, status, ScanReplayEntries(token));
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.Text.Json.JsonException)
                { return Failed("REPLAY FILE OPERATION FAILED // CHECK FILE ACCESS AND REFRESH"); }
            }
        }, token);

    private static RmlUiReplayWorkResult WorkVirtualReplay(RmlUiReplayWorkRequest request,
        RmlUiReplayEntry entry, CancellationToken token)
    {
        if (request.Work == RmlUiReplayWork.Recover)
            return Failed("VIRTUAL CLIPS ARE REFERENCES // RECOVER THE SOURCE RECORDING FIRST");
        if (request.Work == RmlUiReplayWork.Export)
            return Failed("VIRTUAL CLIP FILE EXPORT IS NOT AVAILABLE IN NATIVE THEATRE");
        if (!RmlUiVirtualReplayLibrary.SafeArtifacts(entry, DemoLibrary.Directory))
            return Failed("VIRTUAL CLIP FILES CHANGED OR USE SYMBOLIC LINKS // REFRESH");
        string status;
        switch (request.Work)
        {
            case RmlUiReplayWork.Validate:
                var prepared = RmlUiVirtualReplayLibrary.Resolve(entry, DemoLibrary.Directory, token, out var open);
                if (prepared == null) return Failed("VIRTUAL CLIP PREPARATION FAILED // " + open.ToString().ToUpperInvariant());
                var validation = ReplayArchive.Validate(prepared.Value.PlaybackPath);
                token.ThrowIfCancellationRequested();
                if (!RmlUiVirtualReplayLibrary.PreparedUnchanged(prepared.Value, entry, DemoLibrary.Directory))
                    return Failed("VIRTUAL CLIP OR SOURCE CHANGED // REFRESH");
                status = "VIRTUAL CLIP INTEGRITY // " + validation.ToString().ToUpperInvariant();
                break;
            case RmlUiReplayWork.Rename:
                if (String.IsNullOrWhiteSpace(request.Inputs.Name) || request.Inputs.Name.Length > 100)
                    return Failed("USE A NAME BETWEEN 1 AND 100 CHARACTERS");
                if (!ReplayVirtualClips.Rename(entry.Path, request.Inputs.Name)) return Failed("VIRTUAL CLIP RENAME FAILED");
                status = "VIRTUAL CLIP RENAMED // SOURCE PRESERVED";
                break;
            case RmlUiReplayWork.Organize:
                string tags = request.Inputs.Tags ?? "", collections = request.Inputs.Collections ?? "";
                if (tags.Length > 1024 || collections.Length > 1024) return Failed("LABEL LIST IS TOO LONG");
                if (!RmlUiVirtualReplayLibrary.SafeSidecar(entry.Path + ReplayAnnotations.Extension))
                    return Failed("VIRTUAL CLIP ORGANIZATION SIDECAR IS A SYMBOLIC LINK");
                ReplayAnnotations.SetOrganization(entry.Path,
                    tags.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
                    collections.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
                status = "VIRTUAL CLIP ORGANIZATION SAVED";
                break;
            case RmlUiReplayWork.Delete:
                if (DemoRecorder.IsRecording) return Failed("STOP RECORDING BEFORE DELETING LIBRARY FILES");
                ReplayVirtualClips.Delete(entry.Path);
                status = "VIRTUAL CLIP AND OWNED CACHE DELETED // SOURCE PRESERVED";
                break;
            default: return Failed("VIRTUAL CLIP OPERATION UNAVAILABLE");
        }
        token.ThrowIfCancellationRequested();
        return new(true, status, ScanReplayEntries(token));
    }

    internal static void CopyReplay(string source, string destination, CancellationToken token, bool validate = false)
    {
        token.ThrowIfCancellationRequested();
        string temporary = destination + ".importing";
        bool ownsTemporary = false;
        try
        {
            using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                ownsTemporary = true;
                byte[] buffer = new byte[128 * 1024];
                int length;
                while ((length = input.Read(buffer, 0, buffer.Length)) > 0)
                { token.ThrowIfCancellationRequested(); output.Write(buffer, 0, length); }
                output.Flush(flushToDisk: true);
            }
            token.ThrowIfCancellationRequested();
            if (validate && ReplayArchive.Validate(temporary) != ReplayOpenResult.Success)
                throw new InvalidDataException("Replay import failed validation.");
            token.ThrowIfCancellationRequested();
            File.Move(temporary, destination, overwrite: false);
        }
        finally { if (ownsTemporary && File.Exists(temporary)) File.Delete(temporary); }
    }

    private static RmlUiReplayWorkResult Failed(string message) => new(false, message, Array.Empty<RmlUiReplayEntry>());

    public bool RevealReplayFolder()
    {
        try
        {
            Directory.CreateDirectory(DemoLibrary.Directory);
            return System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(DemoLibrary.Directory)
                { UseShellExecute = true }) != null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        { return false; }
    }
}
#endif
