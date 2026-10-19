"""Link actual retained owners; only native host/world interfaces are synthetic in this fixture."""
from pathlib import Path
import os
root = next((p for p in Path(__file__).resolve().parents if (p / 'src/MphRead').exists()), None)
if root is None:
    raise RuntimeError('Repository root with src/MphRead is unavailable')
out = Path(__file__).resolve().parent / 'obj/source-linked'
out.mkdir(parents=True, exist_ok=True)
studio = (root / 'src/MphRead/Mods/Replay/ReplayStudio.cs').read_text()
body = studio[studio.index('    internal enum ReplayHighlightKind'):studio.index('    internal static class ReplayVirtualClips')]
(out / 'Studio.cs').write_text('using System; using System.Collections.Generic; using System.Linq; using MphRead.Mods.Network; namespace MphRead.Mods.Replay {\n'+body+'\n}')
workspace = (root / 'src/MphRead/Mods/Replay/ReplayWorkspace.cs').read_text()
body = workspace[workspace.index('    internal enum ReplaySegmentCamera'):workspace.index('    internal readonly record struct ReplayExportPreset')]
(out / 'Reels.cs').write_text('using System; using System.IO; using System.Collections.Generic; using System.Text.Json; using System.Linq; using MphRead.Mods.Network; namespace MphRead.Mods.Replay {\n'+body.replace('internal static class ReplayReels','internal static partial class ReplayReels')+'\n}')
metadata = (root / 'src/MphRead/Mods/Network/ReplayMetadata.cs').read_text()
body=metadata[metadata.index('    public enum ReplayEventType'):metadata.index('    internal readonly record struct ReplayExpectedHash')]
(out / 'Metadata.cs').write_text('using System; namespace MphRead.Mods.Network {\n'+body+'\n}')
demo=(root/'src/MphRead/Mods/Network/DemoPlayback.cs').read_text()
body=demo[demo.index('    private static readonly ConditionalWeakTable<object, object> PlaybackOwnerTokens'):demo.index('    internal static Scene? ReplicaScene')]
(out/'Identity.cs').write_text('using System.Runtime.CompilerServices; namespace MphRead.Mods.Network { internal static partial class DemoPlayback {\n'+body+'\n} }')
