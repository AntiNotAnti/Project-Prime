# Portable settings archives

`ProjectPrime -settingsarchive export ProjectPrime-settings.zip` exports saved
preferences. `import` validates and installs an archive; `validate` performs no
writes. Commands run before game-file setup and require no extracted content.
The launcher exposes Export Settings, Import Settings, Reset Settings and (desktop)
Open Settings Folder under Settings / System. Apply or discard pending edits first.
Import/reset are unavailable inside a match. Reset has an explicit confirmation and
only removes registered preferences plus normal HUD `.json.bak` fallback copies;
account stores, game data and transaction recovery files remain untouched.

Import and reset offer Restart Now / Restart Later. Both keep the current runtime
configuration until a fresh process starts. All registered preference writers are
serialized against the archive transaction and fenced off after success, so a later
window close, controller save or HUD save cannot replace imported/reset files with
old in-memory values. Reopening Settings shows the restart controls. Failed imports
with successful rollback release the fence; incomplete rollback retains it and
reports recovery files. Desktop restart launches the same executable and requests
normal shell shutdown. Android uses the system ZIP document picker for import/export
(no storage permission), and schedules a fresh process through the OS alarm service
before closing the current one. OS launch timing and document-provider behavior need
physical-device acceptance; if launch is delayed, opening the app manually uses the
saved settings. Cancelled document selection makes no preference changes.

The format-1 ZIP contains `manifest.json` and only registered stores:

- `Savedata/settings.json` (graphics, renderer and general game preferences)
- `launcher.txt` (launcher, sound selections and replay preferences)
- `controls.txt` (input, controller bindings and Touch Studio)
- `controller-profiles.json` and `gamecontrollerdb.txt`
- `Savedata/hud-profiles/<portable-name>.json` (HUD, crosshair and radar)

The root is `LauncherPrefs.Directory`, the platform's writable data directory.
No recursive root discovery occurs. Saves, account stores, credentials, game data,
replays, project state, caches and recovery files are not registered. Recognized
credential fields inside an otherwise valid settings store are rejected too.

Limits: 128 stores, 16 MiB total uncompressed, 20 MiB archive input, 64 KiB manifest,
and per-store limits. All text uses strict UTF-8. Paths are case-sensitive canonical
registry names; aliases, duplicate entries, traversal, links and special files are
rejected. Named HUD profiles follow the normal store's portable-name policy.

Import validates all bytes first, writes same-directory temporary files, snapshots
all originals, then replaces each file. An installation failure restores prior
files and removes newly installed stores. If rollback itself encounters an I/O
failure, remaining recovery files are retained and an explicit aggregate error is
returned. This is a rollback transaction for reported I/O failures, not a claim of
cross-file crash atomicity. Do not run import concurrently with another game
process writing the same data directory.

Missing optional stores remain unchanged. Retired keys follow the existing
loaders' forward/backward compatibility behavior. HUD, controller-library and
settings JSON use the production parsers/migrations on detached data. Legacy
text stores use a shared, side-effect-free grammar also consumed by the runtime
loaders. Known launcher/input/touch/controller scalar, enum, binding and layout
values are checked before installation; controller migration parsers run on detached
instances. Unknown/retired keys remain compatible and the final duplicate wins.
Mapping validation shares the production mapping grammar without calling GLFW.

Run `dotnet run --project tools/settings-archive-check -c Release` for content-free
round-trip, canonical writer compatibility, malformed archive, path/link, limits,
old settings and injected rollback checks.

Add `-- --ui` to exercise reset cancellation and restart controls using the managed
Avalonia UI. Physical Android document-provider/restart and controller/touch focus
acceptance remain manual platform checks. This is not the whole seven-feature initiative.
