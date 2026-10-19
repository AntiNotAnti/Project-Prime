#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Launcher.Core;

namespace MphRead.Mods.Launcher.RmlUi.Render;

internal sealed class NativeGameFilesService : INativeGameFilesAuthority
{
    private NativeGameFilesState? _last;
    private static int _pickerInProgress;
    public NativeGameFilesState Read()
    {
        // Problem/ApplyPaths mutate the existing path selection; never invoke
        // them concurrently with extraction. UI state remains detached while busy.
        if (GameFiles.TryInspectSetup(() =>
        {
            string? problem = GameFiles.Problem();
            bool ready = problem == null, previews = ready && ThumbnailHost.CanRender;
            int missing = previews ? ThumbnailGenerator.MissingThumbnails().Count : 0;
            return new NativeGameFilesState(ready, problem ?? GameFiles.Describe(), GameFiles.Root,
                NativeFilePicker.Available, previews, missing);
        }, out NativeGameFilesState fresh)) return _last = fresh;
        return (_last ?? new(false, "Game-file setup is working.", GameFiles.Root,
            NativeFilePicker.Available, false, 0)) with { SetupBusy = true };
    }
    public async Task<NativeGameFilesPick?> PickAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!NativeFilePicker.Available) return new("", "", "", "This desktop has no file dialog. Install zenity or kdialog and try again.");
        if (Interlocked.CompareExchange(ref _pickerInProgress, 1, 0) != 0)
            return new("", "", "", "A system file dialog is already open. Finish or cancel that dialog first.");
        string? path;
        try { path = await NativeFilePicker.OpenFile("Your Metroid Prime Hunters cartridge dump", "Nintendo DS ROM", "nds").ConfigureAwait(false); }
        finally { Volatile.Write(ref _pickerInProgress, 0); }
        token.ThrowIfCancellationRequested(); if (path == null) return null;
        return await Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            bool valid = RomCompatibility.TryIdentify(path, out string? label, out string? problem);
            token.ThrowIfCancellationRequested();
            return new NativeGameFilesPick(path, Path.GetFileName(path), label ?? "", valid ? "" : problem ?? "That cartridge dump is unavailable.");
        }, token).ConfigureAwait(false);
    }
    public async Task<NativeGameFilesResult> InstallAsync(string path, string expectedRoot, Action<string> report, CancellationToken token)
    {
        bool success = await GameFiles.RunSetupAsync(path, report, expectedRoot, token).ConfigureAwait(false);
        return new NativeGameFilesResult(success, success ? "Ready to play." : "Setup did not finish. Check the selected file and game-files status.");
    }
    public async Task<NativeGameFilesResult> PreviewsAsync(Action<string> report, CancellationToken token)
    {
        string root = _last?.Root ?? GameFiles.Root;
        token.ThrowIfCancellationRequested();
        if (GameFiles.SetupInProgress || !String.Equals(root, GameFiles.Root, StringComparison.Ordinal))
            return new(false, "Game files changed or setup is working. Check game files first.");
        int count = await ThumbnailHost.RenderMissingAsync(report, token).ConfigureAwait(false);
        return new(true, $"Rendered {count} missing map previews.");
    }
}
#endif
