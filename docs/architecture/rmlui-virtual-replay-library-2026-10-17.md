# Native Theatre virtual replay library

The native Theatre now projects `.ppclip` descriptors created by the existing range panel into its replay rows. A virtual row uses the existing `ReplayVirtualClips` owner for listing, parsing, materialization, rename, favorite, and deletion, and `ReplayAnnotations` for organization. It does not introduce another descriptor serializer, replay clock, reader, or playback session.

Watch prepares the existing `.virtual-cache/*.ppdemo` on `ReplayStorageJobs`, runs the shared replay launch probe, and rechecks the selected descriptor, source facts, and prepared cache before the existing Shell launch handoff. This matches the established Theatre contract: `DemoPlayback` consumes the materialized recording, while `ReplayVirtualClips.LogicalPath` maps it back to the descriptor so bookmarks/camera edits retain their logical owner.

## Boundaries and ownership

- The row carries an optional immutable witness: descriptor SHA-256 plus source path, length, and last-write time. Physical replay constructors and rows retain their prior shape/behavior.
- Descriptors are bounded to 64 KiB. Descriptor hashing runs during worker scans/preflight or explicit actions; no source recording is hashed by production listing, preflight, launch, Tick, or Draw.
- Descriptor and source must be regular top-level files of the expected type in the configured local library. Direct descriptor/source file links, direct library/cache-directory links, and relevant sidecar links are refused.
- Existing directory ancestry, including ancestors within a link target, is resolved at the snapshot/action boundary with a 40-link bound. The source witness pins that resolved library path, permitting stable platform aliases while rejecting a later ancestor redirect that presents matching descriptor/source file facts.
- A bounded process-local cache witness remembers at most 64 prepared recordings. Unknown or stale native cache contents are removed through the existing `ReplayArtifacts.DeleteCache` owner before `ResolveForPlayback` reconstructs the selected range. This covers a descriptor range change whose file timestamp was preserved without replacing the shared materializer.
- Delete removes the descriptor and its owned cache/sidecars. It never deletes the referenced physical recording. Missing-source descriptors remain visible and can be renamed, organized, or removed safely.

Virtual recovery is unavailable: recovery belongs to the physical source recording. Native virtual replay-file export is explicitly unsupported in this slice; it returns a clear status and never copies JSON under a `.ppdemo` filename. The shared native menu binding disables Export for virtual rows.

Cancellation is checked before and after shared synchronous materialization and before publishing prepared launch data. It cannot interrupt `ReplayVirtualClips.ResolveForPlayback` while that owner is extracting; a valid owned cache may remain after cancellation. Stale controller completions retain the existing generation/selection guards, and virtual selection checks additionally compare the immutable witness.

The source witness does **not** establish a content digest: same-length source byte changes with preserved mtime are outside its scope. File checks before/after an operation also do not claim an atomic filesystem security boundary against another process swapping paths between those checks.

The shared native annotation witness now also includes bounded raw descriptor
SHA-256 in its existing recording revision string. Bookmarks, highlights and
range creation therefore reject a changed `.ppclip` range even when its length
and timestamp were preserved. Camera validation can reuse the same helper.
This work changes no context shape or serializer; hashing occurs only at
explicit capture/validation, while readonly `IsCurrent` checks remain free of
file I/O. Physical recording revisions retain their length/mtime contract.

## Evidence

The first coordinated build passed `--replay-files` with **29 production tempfile checks**. The fixture creates its own small `.ppdemo`, calls the actual virtual serializer, native library adapter, shared materializer/probe, and file-operation paths, and restores the process-local Export setting exactly. It covers logical annotations, favorite, rename/organization, descriptor SHA changes at matching length/mtime, ordinary source-fact changes, changed-range cache reconstruction, out-of-library pointers, direct descriptor/source/sidecar/cache links, missing sources, cancellation, unsupported export/recovery, and physical-source byte/timestamp preservation.

The same 29 file checks also passed alongside **72 native Offline Rules checks** at each actual framebuffer size **640×360, 1280×720, and 1920×1080**. Those framebuffer runs validate the existing native rules UI; they do not establish an asset-backed virtual clip watch or a native library click journey.

Evidence: [acceptance record](</Users/jarrett/Documents/Development/Prime Hunters Online/artifacts/rmlui-offline-rules-rml42/virtual-replay-acceptance.json>) and its adjacent `virtual-replay-files-headless.log` / `virtual-offline-actual*.log` files.

The final coordinated build passed **all 33 production tempfile checks**, including configured-ancestor retarget rejection, stable pre-existing alias acceptance, aliases within a link target's ancestry, and retargeting those nested aliases. An isolated .NET 10 experiment confirmed that `ResolveLinkTarget(true)` alone retains aliases in a returned target's parent path; the helper now resolves those parents recursively. The final headless log is `/tmp/prime-rmlui-replay-files-ancestry-final.log`; the framebuffer evidence above remains the earlier 29-check run.

The subsequent shared annotation helper passed **9 source-linked production
helper checks**, with unused replay/scene facades supplied by the isolated
fixture: physical compatibility, exact descriptor SHA, same-size/same-stamp range
changes, restored bytes, uppercase extension, size bound, empty/missing input
and symbolic links. Log: `/tmp/prime-annotation-witness-source.log`. Five matching
reflection checks then passed in the production file fixture: **38 actual
production tempfile checks** in the immutable security/studio checkpoint.
Evidence: `artifacts/rmlui-checkpoint-20261017-security-studio/replay-files.log`
and the `headlessReplayFiles` entry in its `acceptance.json`. This is headless
file-helper evidence; the checkpoint separately records pending visual issues
in other UI surfaces.

No real replay, preference, account, network session, or game world is used by the file fixtures. Full native library interaction and asset-backed virtual playback acceptance remain separate device/content gates.
