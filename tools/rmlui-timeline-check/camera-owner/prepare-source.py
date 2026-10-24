"""Compile retained camera capture/format owners with synthetic world/transport boundaries."""
from pathlib import Path

root = next(p for p in Path(__file__).resolve().parents if (p / "src/MphRead").exists())
sources = root / "src/MphRead/Mods"
out = Path(__file__).resolve().parent / "obj/source-linked"
out.mkdir(parents=True, exist_ok=True)
native = (sources / "Replay/ReplayCamera.Native.cs").read_text()
start = native.index("namespace MphRead.Mods.Replay")
end = native.index("\nnamespace MphRead\n", start)
(out / "NativeOwner.cs").write_text(
    "using System; using System.IO; using System.Linq; using MphRead.Mods.Network;\n"
    + native[start:end]
)
start = native.index("        internal bool TryCaptureNativeReplayCamera")
end = native.index("\n    }\n}", start)
(out / "NativeCapture.cs").write_text(
    "using System; using MphRead.Mods.Network; namespace MphRead { public partial class Scene {\n"
    + native[start:end] + "\n} }"
)
workspace = (sources / "Replay/ReplayWorkspace.cs").read_text()
reels = workspace[workspace.index("    internal enum ReplaySegmentCamera"):
                  workspace.index("    internal static partial class ReplayReels")]
annotations = (sources / "Replay/ReplayAnnotations.cs").read_text()
annotations = annotations[annotations.index("    internal sealed record ReplayBookmark"):
                          annotations.index("    /// <summary>")]
studio = (sources / "Replay/ReplayStudio.cs").read_text()
clip = studio[studio.index("    internal sealed record ReplayVirtualClipDocument"):
              studio.index("    internal static class ReplayVirtualClips")]
(out / "Types.cs").write_text(
    "using System; using System.Collections.Generic; namespace MphRead.Mods.Replay {\n"
    + reels + annotations + clip + "\n}"
)

legacy = (sources / "Replay/ReplayCamera.cs").read_text()
start = legacy.index("        private static bool EditTrack")
end = legacy.index("        internal static void SaveKeyframe", start)
(out / "NativeLegacyGuard.cs").write_text(
    "using System; namespace MphRead.Mods.Replay { public static partial class ReplayCamera {\n"
    + legacy[start:end] + "\n internal static bool FixtureLegacySave()=>EditTrack(track=>track.Remove(10));\n} }"
)
