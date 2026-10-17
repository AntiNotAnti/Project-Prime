# RML-28: native Replay/Theatre archive

## Scope

Restore a practical native replay library before migrating full Settings.
The authored October 9 Theatre layout is modernized as a truthful, bounded
archive. The existing DemoLibrary, ReplayStorageJobs, replay parser, replay
compatibility policy, MapIdentity checker and StartScreen remain authoritative.
There is no second replay reader, editor, network protocol or rendering process.

## Native archive

- Read recording metadata with DemoLibrary.List under the existing bounded
  ReplayStorageJobs worker pool. Only completion summaries enter the RmlUi
  owner thread; no full replay decoding in the UI render loop.
- Display 8 results per page, cap active index at 8192 records, support a
  512-character search, 6 useful filters and 4 sorts, and preserve selected
  file identity only while it remains present in the latest library snapshot.
- Toggle Favorite via the existing marker persistence. Refresh the archive
  from disk after the action rather than optimistically inventing metadata.
- Before Watch, recheck a locally listed replay's exact directory, extension,
  size and write timestamp. Offload the existing DemoReader metadata/protocol
  and ReplayMapIdentity check to ReplayStorageJobs, now shared with the
  legacy Theatre workspace through ReplayLaunchValidator.
- Launch a valid recording through the existing StartScreen.Finish Demo path,
  only once and only after the exact indexed selection remains current.
- Navigation and cancelled worker results are epoch-fenced. The native
  document owns search text and typed foreground events, never a hidden
  bootstrap Home document.

## Deliberate compatibility

The full replay workspace still owns richer operations such as virtual clips,
video export, thumbnail management, validation/recovery, annotations, import,
batch actions, delete/rename, and Studio editing. Native Import and
Open Full Replay Studio open that established route rather than presenting
inert UI controls or duplicating disk mutation logic.

A real-device/desktop frame acceptance pass remains required. The default
Avalonia UI stays unchanged. Full Settings remains **LAST**, after the
rest of the non-Settings pages and Android lifecycle/input verification.

## Verification

Asset-free live menu acceptance covers search, filters, sorting, pagination,
selection stamps, favorite refresh, async replay validation, cancelled late
completion, exact one-shot launch and full Studio fallback.
The native runtime fixture verifies real page ownership, scoped search,
rejected retired document events and Back to Home.

CI must pass the no-game-assets runner, native software OpenGL acceptance,
desktop matrix, legacy Theatre and replay compatibility checks before merge.
