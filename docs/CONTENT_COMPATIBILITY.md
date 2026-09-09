# Content compatibility

Project Prime separates the content needed to run a match from local
presentation choices. QZ6 performs bounded local discovery, saves an explicit
announcer and music choice, and injects those selections into each new client
presentation. It does not add a downloader.

## Compatibility authority

The existing `Server.Shared.ContentIdentity` and the Worker-captured content
hash remain the required gameplay admission key.  A server still rejects a
client/Worker with a required map, content version, or content hash mismatch.
`GameplayContentManifest` is a bounded local description of the same required
content and an integrity declaration; it is not added to `MatchSpec` and it is
not folded into the existing Worker hash.

The gameplay manifest contains:

- a stable pack id, version, and SHA-256 content identity;
- separate map, collision, entity, and gameplay-data identities; and
- a bounded list of relative files with byte counts and SHA-256 hashes.

Installed gameplay selection is exact on stable id, version, and hash.  No
near-match is accepted.  `InstalledContentCatalog.RequireGameplay` therefore
fails closed with `RequiredContentMismatchException` when the requested pack is
missing, malformed, or has a different identity.

## Optional presentation packs

`OptionalPresentationManifest` supports only these kinds:

| Kind | Allowed file extensions | Event mappings |
| --- | --- | --- |
| `announcer` | `.wav`, `.ogg`, `.mp3` | known announcer event keys |
| `music` | `.wav`, `.ogg`, `.mp3`, `.flac` | none |
| `hudTheme` | `.json`, `.png`, `.jpg`, `.jpeg`, `.webp` | none |
| `cosmeticEffects` | `.json`, `.png`, `.jpg`, `.jpeg`, `.webp` | none |

The current announcer mapping keys are `three`, `two`, `one`, `go`, `overtime`,
`matchPoint`, `victory`, `defeat`, `firstHunt`, `doubleKill`, `tripleKill`,
`interceptor`, `defender`, `primeSlayer`, `capture`, and `assist`.  This is the
stable shape consumed by the QZ3 announcer. A selected custom mapping resolves
only a file declared by that pack. Missing mappings and decode/device failures
fall back to the built-in cue.

A music pack's manifest file order is its bounded playlist. The client chooses
one entry deterministically from the room id/music variant or later requested
music id, loops it through the existing SoundFlow output, and falls back to the
built-in sequence if the declared file cannot be resolved or decoded. This
selection is entirely presentation-side.

Optional identities are never part of `MatchSpec`, `ContentIdentity`, the
Worker content hash, gameplay data, collision, radar, weapon, or objective
rules.  Two clients may select different valid optional packs while joining the
same gameplay content.  If a requested optional pack is absent or invalid,
`InstalledContentCatalog.SelectOptional` returns a built-in/default selection
with `UsedFallback = true`.

## Installation and selection

Install each pack as a direct child directory of `content` beside the client
executable (the same portable directory that owns `launcher.txt`). A directory
is considered installed only after its manifest and every declared file pass
the checks below.

Open **Settings → Audio → Presentation packs** to select an installed announcer
and music pack. `Built-in` is always available. The launcher saves the exact
stable id, version, and SHA-256 pack identity rather than a display name or file
path. Choices apply to the next match, when `ScenePresentation` performs a new
bounded discovery pass and receives one immutable selection snapshot.

If a saved identity is missing, changed, malformed, or of the wrong kind, the
corresponding presentation uses built-in content. This does not prevent joining
a compatible server and does not alter the other optional selection.

## Local discovery and integrity

`InstalledContentCatalog.Discover` examines only local directories.  It never
opens a network connection or attempts an automatic download.  A pack directory
contains exactly one of:

- `gameplay-manifest.json`, or
- `optional-presentation-manifest.json`.

The manifest reader rejects duplicate JSON properties, unknown members,
comments, trailing commas, malformed values, and excessive JSON depth.  The
catalog bounds manifest size, file count, path length, individual file bytes,
total file bytes, filesystem entries, and installed-pack count.

Paths must be safe relative paths.  Rooted paths, drive/colon syntax,
backslashes, empty/dot/traversal components, controls, wildcards, query-like
characters, and canonical paths outside the pack root are rejected.  The
validator checks every declared file's size and SHA-256, rejects unlisted
files, and rejects symbolic links/reparse points before descending or opening
the file.  Optional files are restricted to their known presentation
extensions, so assemblies, native libraries, scripts, and executable files do
not enter the presentation catalog.

Invalid optional directories are reported as discovery issues and are eligible
for fallback.  Invalid required directories are never selected by the exact
required matcher; admission remains fail-closed.

The runtime resolver accepts only a manifest-relative key already declared by
the selected pack. It never accepts an absolute path or URI and rechecks root
containment, symbolic-link status, existence, declared byte length, and SHA-256
at consumption. The hash is calculated through the same bounded file handle
handed to the audio decoder, so a same-size replacement after discovery fails
closed instead of creating a validation-to-open window. This work stays in the
client presentation/audio path and never blocks an authoritative gameplay or
network thread. Assemblies, native libraries, scripts, URLs, and
download/extraction hooks have no optional-content execution path.

## JSON shape

The canonical JSON uses camel-case member names and string enum values.  A
minimal required declaration is:

```json
{
  "format": 1,
  "stableId": "community-arena",
  "version": "1.0.0",
  "contentHash": "<64 hexadecimal characters>",
  "gameplay": {
    "map": "arena-map-v1",
    "collision": "arena-collision-v1",
    "entities": "arena-entities-v1",
    "gameplayData": "arena-rules-v1"
  },
  "files": [
    { "path": "maps/arena.bin", "bytes": 1234, "sha256": "<64 hexadecimal characters>" }
  ]
}
```

An optional announcer declaration uses the same identity/file shape plus
`kind: "announcer"` and an `events` array of `{ "key", "path" }` mappings.
Every mapped path must also be declared in `files`.

A music declaration uses `kind: "music"`, declares one or more supported audio
files in playlist order, and supplies an empty `events` array.
