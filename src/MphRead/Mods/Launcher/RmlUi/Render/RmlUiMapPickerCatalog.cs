#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Launcher.RmlUi.Core;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.Launcher.RmlUi.Render;

internal static class RmlUiMapPickerCatalog
{
    // Archive verification is serialized off the compositor, including repeated
    // open/close requests. A canceled picker never adopts the resulting rows.
    private static readonly SemaphoreSlim Verification = new(1, 1);
    internal static Task<IReadOnlyList<RmlUiArenaChoice>> ReadAsync(string root, CancellationToken token)
        => Task.Run<IReadOnlyList<RmlUiArenaChoice>>(async () =>
        {
            await Verification.WaitAsync(token).ConfigureAwait(false);
            try
            {
                using var files = GameFiles.TryUseConfiguredFiles(root)
                    ?? throw new InvalidOperationException("Game files changed.");
                var rows = new List<RmlUiArenaChoice>();
                var registry = CustomRooms.Installed;
                foreach (var map in CustomRooms.Definitions)
                {
                    token.ThrowIfCancellationRequested();
                    var category = RmlUiMapPickerModel.Group(map.Name, map.InGameName, map.Description, map.Import != null);
                    if (map.RequiresPrivateSource)
                    {
                        if (category == RmlUiArenaCategory.Echoes
                            && Import.EchoesOfflinePlaytestCatalog.IsQualifiedInstalled(map, root))
                            rows.Add(new(map.Name, category, false, "OFFLINE PLAYTEST // MULTIPLAYER UNAVAILABLE"));
                        continue;
                    }
                    if (!registry.TryGet(map.Name, out var installed) || !registry.HasExact(installed.Identity)) continue;
                    rows.Add(new(map.Name, category, true, category == RmlUiArenaCategory.Community
                        ? "INSTALLED PACKAGE" : category.ToString().ToUpperInvariant() + " // INSTALLED PACKAGE"));
                }
                token.ThrowIfCancellationRequested();
                if (root != GameFiles.Root) throw new InvalidOperationException("Game files changed.");
                return rows.ToArray();
            }
            finally { Verification.Release(); }
        }, token);
}
#endif
