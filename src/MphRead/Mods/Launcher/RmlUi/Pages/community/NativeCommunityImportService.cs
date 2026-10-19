#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.Launcher.RmlUi.Pages.Community;

internal sealed class NativeCommunityImportService : INativeCommunityImportAuthority
{
    private sealed record Context(string Root);
    public bool PickerAvailable => NativeFilePicker.Available;
    public object? CaptureContext()
    {
        if (!GameFiles.Ready) return null;
        MapRuntimeUsage.RequireInstallationAllowed();
        return new Context(GameFiles.Root);
    }
    public bool IsCurrentContext(object context) => context is Context current
        && current.Root == GameFiles.Root && GameFiles.Ready;

    public async Task<INativeCommunityPreparedImport?> PrepareAsync(string path, bool usePicker, object context, CancellationToken token)
    {
        if (context is not Context current || !IsCurrentContext(current)) throw new InvalidOperationException("Game files changed.");
        if (usePicker)
        {
            if (!PickerAvailable) throw new InvalidOperationException("File picker unavailable.");
            path = await NativeFilePicker.OpenFile("Import Community map package", "Project Prime map package", "ppmap").WaitAsync(token).ConfigureAwait(false) ?? "";
            token.ThrowIfCancellationRequested(); if (path.Length == 0) return null;
        }
        path = path.Trim();
        if (path.Length >= 2 && path[0] == '"' && path[^1] == '"') path = path[1..^1];
        path = Path.GetFullPath(path);
        if (!MapBundle.Is(path) || !File.Exists(path)) throw new InvalidDataException("Choose an existing .ppmap package.");
        IDisposable? files = GameFiles.TryUseConfiguredFiles(current.Root)
            ?? throw new InvalidOperationException("Game files are being changed.");
        PreparedMapInstallation? prepared = null;
        try
        {
            token.ThrowIfCancellationRequested(); MapRuntimeUsage.RequireInstallationAllowed();
            MapContentIdentity identity = MapContentIdentity.FromPackage(path);
            // The existing installer snapshots caller-owned bytes and builds privately.
            // Confirmation publishes that exact snapshot, even if the caller edits the original.
            prepared = await MapPackageInstaller.PrepareAsync(path, identity, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (!IsCurrentContext(current)) throw new InvalidOperationException("Game files changed.");
            var result = new Prepared(this, current, files, prepared);
            files = null; prepared = null; return result;
        }
        finally { prepared?.Dispose(); files?.Dispose(); }
    }

    private sealed class Prepared : INativeCommunityPreparedImport
    {
        private readonly NativeCommunityImportService _owner;
        private readonly Context _context;
        private readonly IDisposable _files;
        private readonly PreparedMapInstallation _installation;
        private bool _disposed, _committed;
        public string Name => _installation.Identity.RoomKey;
        public string Version => "";
        public string PackageHash => _installation.Identity.PackageHash.ToString();
        public bool IsCurrent => !_disposed && !_committed && _owner.IsCurrentContext(_context);
        internal Prepared(NativeCommunityImportService owner, Context context, IDisposable files, PreparedMapInstallation installation)
            => (_owner, _context, _files, _installation) = (owner, context, files, installation);
        public void Commit()
        {
            if (!IsCurrent) throw new InvalidOperationException("Import context changed.");
            MapRuntimeUsage.RequireInstallationAllowed();
            MapDefinition definition = _installation.Commit(CustomRooms.UserMapDirectory);
            Metadata.RegisterDownloadedMap(definition); _committed = true;
        }
        public void Dispose()
        {
            if (_disposed) return; _disposed = true;
            try { _installation.Dispose(); } finally { _files.Dispose(); }
        }
    }
}
#endif
