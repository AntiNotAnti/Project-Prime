from pathlib import Path
root = Path(__file__).resolve().parents[2]
out = Path(__file__).resolve().parent / "obj" / "source-linked"
out.mkdir(parents=True, exist_ok=True)
workspace = (root / "src/MphRead/Mods/Replay/ReplayWorkspace.cs").read_text()
queue = workspace[workspace.index("    internal static partial class ReplayExportQueue"):workspace.index("    internal sealed record ReplayVideoSegment")]
(out / "Queue.cs").write_text("using System; using System.Collections.Generic; using System.Linq; namespace MphRead.Mods.Replay {\n" + queue + "\n}")
exporter = (root / "src/MphRead/Mods/Replay/ReplayVideoExporter.cs").read_text()
exporter = exporter[:exporter.index("        // Three scene stills")]
(out / "Exporter.cs").write_text(exporter + "        private static void CaptureReplayThumbnails(Scene scene) {}\n    }\n}\n")

studio = (root / "src/MphRead/Mods/Replay/ReplayStudio.cs").read_text()
types = studio[studio.index("    internal enum ReplayVideoResolution"):studio.index("    internal static partial class ReplayVideoExport")]
segments = workspace[workspace.index("    internal sealed record ReplayVideoSegment"):workspace.index("    internal static partial class ReplayVideoExport")]
presets = workspace[workspace.index("    internal readonly record struct ReplayExportPreset"):workspace.index("    internal static partial class ReplayExportQueue")]
reels = workspace[workspace.index("    internal enum ReplaySegmentCamera"):workspace.index("    /// <summary>")]
virtual = studio[studio.index("    internal sealed record ReplayVirtualClipDocument"):studio.index("    internal static class ReplayVirtualClips")]
annotations = (root / "src/MphRead/Mods/Replay/ReplayAnnotations.cs").read_text()
annotations = annotations[annotations.index("    internal sealed record ReplayBookmark"):annotations.index("    /// <summary>")]
(out / "Types.cs").write_text("using System; using System.Collections.Generic; namespace MphRead.Mods.Replay {\n" + types + segments + presets + reels + virtual + annotations + "\n}")
for name, source in (("ManifestCreate.cs", studio), ("PresetCreate.cs", workspace)):
    body = source[source.index("    internal static partial class ReplayVideoExport"):]
    (out / name).write_text("using System; using System.IO; using System.Linq; using System.Collections.Generic; using System.Text.Json; using MphRead.Mods.Network; namespace MphRead.Mods.Replay {\n" + body)
demo = (root / "src/MphRead/Mods/Network/DemoPlayback.cs").read_text()
identity = demo[demo.index("    private static readonly ConditionalWeakTable<object, object> PlaybackOwnerTokens"):demo.index("    internal static Scene? ReplicaScene")]
(out / "Identity.cs").write_text("using System.Runtime.CompilerServices; namespace MphRead.Mods.Network { internal static partial class DemoPlayback {\n" + identity + "\n} }")

# Link actual immutable audio inputs and validation, without pulling mixer/device workers into this owner fixture.
audio = (root / "src/MphRead/Mods/Replay/ReplayExportAudio.cs").read_text()
(out / "AudioInput.cs").write_text(audio[:audio.index("/// <summary>Only storage workers")] + "\n#endif\n")
mix = (root / "src/MphRead/Mods/Replay/ReplayStudioAudioMix.cs").read_text()
(out / "AudioValidation.cs").write_text(mix[:mix.index("    internal static void AppendCustomCues")] + "}\n#endif\n")
audio_core = (root / "src/MphRead/Mods/Launcher/Core/TheatreStudioAudioSettings.cs").read_text()
audio_core = audio_core[audio_core.index("public sealed record TheatreStudioAudioSettings"):audio_core.index("public interface ITheatreStudioAudioSettingsBackend")]
tools = (root / "src/MphRead/Mods/Launcher/Core/TheatreStudioToolsContracts.cs").read_text()
cue = tools[tools.index("public sealed record TheatreAudioCue("):tools.index("public sealed record TheatreAudioEventValues")]
(out / "AudioContracts.cs").write_text("using System.Collections.Generic; namespace MphRead.Mods.Launcher.Core;\n" + audio_core + cue)
metadata = (root / "src/MphRead/Mods/Network/ReplayMetadata.cs").read_text()
metadata = metadata[metadata.index("    public enum ReplayEventType"):metadata.index("    internal readonly record struct ReplayExpectedHash")]
(out / "Metadata.cs").write_text("using System; namespace MphRead.Mods.Network {\n" + metadata + "\n}")
