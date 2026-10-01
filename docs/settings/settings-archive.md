# Portable settings archives

`ProjectPrime -settingsarchive export ProjectPrime-settings.zip` exports saved
preferences. `import` validates and installs an archive; `validate` performs no
writes. Commands run before game-file setup and require no extracted content.
The launcher exposes Export Settings, Import Settings and Open Settings Folder
under Settings / System. Apply or discard pending edits first. Import is disabled
inside a match, reloads preferences and replaces the editor to prevent stale
controls from overwriting the imported values. Renderer changes require a later
manual restart. Android shares the archive engine; its current UI uses a local
app-accessible path, not the Android document picker.

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
text stores receive syntax and common typed-value checks; they do not yet have
complete detached versions of the stateful runtime parsers.

Run `dotnet run --project tools/settings-archive-check -c Release` for content-free
round-trip, canonical writer compatibility, malformed archive, path/link, limits,
old settings and injected rollback checks.

Remaining roadmap work: complete detached text-store parsing, Reset Settings,
explicit Restart Now/Later controls, native Android document picker and device UI
acceptance. The archive foundation is not the whole seven-feature initiative.
