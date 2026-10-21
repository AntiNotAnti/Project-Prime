# CW-11a — strict local weapon package foundation

`WeaponPackageReference.BuildRiftlineM7()` exports the existing immutable M7
definition and original CC0 model/audio/license/readme. It needs no extracted
game files, graphics, audio device or Scene. `WeaponPackage.Parse` validates a
detached snapshot; `WeaponPackageBuilder.Build` validates source bytes and then
re-reads the actual completed archive. Native registry entries and CWM1 remain
unchanged. Package parsing does not install, equip, grant or activate a weapon.
The existing M7 is still acquired from world pickups.

This first slice supplies the library and diagnostic. CLI/project export,
atomic installation and pinned cache leases follow in CW-11b. Local game test,
package readiness, Community and Studio integration remain subsequent slices;
this document does not claim those flows are delivered.

## Supported version1 capabilities

A self-contained archive contains `manifest.json`, `weapon.json` and every exact
resource referenced by the project. Unknown/duplicate/missing JSON fields,
invalid UTF-8/surrogates, nonfinite numbers and unsupported schemas fail closed.
All JSON has byte, depth and aggregate-node limits. Diagnostics cap untrusted
detail at512 printable ASCII characters before the fixed prefix, including
unknown-field errors. License declarations record the author's statement,
not independent rights verification.

The project requires these fields in its canonical order:

`schema`, `contentId`, `productId`, `name`, `author`, `license`, `readme`,
`mechanics`, `presentation`, `dependencies`.

Content ID is a canonical nonempty lowercase UUID. Product ID is bounded lowercase
ASCII. License has exact `spdx`/`path`; presentation has exact `schema`, `mount`,
`model`, `fallback`, `audio`. Mount is `native-arm-module-v1`, fallback is
`builtin-original-module-v1`, and model is `prime-rigid-boxes-v1` with an exact
JSON path. Its existing complete bounded CPU decoder validates palette, geometry,
groups, muzzle bounds, basis and two LODs. The socket remains cosmetic data.

Audio format `pcm16-mono-22050-v1` requires four distinct shot/bolt/reload/empty
WAV paths. The complete narrow decoder checks RIFF/WAVE, exact44-byte canonical
header, PCM1/mono/16-bit/22050 Hz, byte rate/block alignment, exact file/data
lengths and1–11,025 frames. No compressed/float/extensible WAV, extra chunk,
trailing payload, external stream or callback decoder is accepted.

Mechanics uses the existing schema1/behaviorVersion1 precision parser and
canonicalizer. Dependencies must be `dependencies.lock.json`, with exactly
`{"schema":1,"dependencies":[]}` after normalization. Nonempty dependencies
are explicitly unsupported. Missing dependencies/assets never resolve against
the working directory or some installed revision. Corrupt required assets fail
validation even though the declared renderer fallback exists.

GLB/glTF, textures, scripts, expressions, shaders, plugins/native libraries,
nested archives, arbitrary codecs and external URIs are unsupported. Future
capabilities need their own complete bounded CPU decoder and versioned contract;
a preliminary Community header check does not enable them. Plain license/readme
text can contain ordinary links but is never interpreted or fetched.

## Container and bounds

The intentionally narrow ZIP32 profile accepts stored entries only: version20,
made-by20, flags0, method0, time0/date33 (1980-01-01), zero attributes and no
extras/comments/descriptors/multiple disks/ZIP64. Central/local names, CRCs,
lengths and offsets must agree. Entries are contiguous from offset0, sorted by
ordinal name, followed immediately by the directory and exact22-byte EOCD.
Offsets/counts are checked before slicing or copying. There is no inflater or
filesystem extraction. General ZIP tools may emit unsupported metadata; use
the package builder for this profile.

Limits are8 MiB archive,16 MiB aggregate expanded input,32 entries,128 ASCII bytes
per path, eight path components and64 bytes per component. Absolute/traversal/
backslash/drive/device/hidden/trailing-dot paths, directories, non-ASCII paths,
duplicate names and case aliases are rejected. Every reference is exact-case;
no basename/suffix lookup occurs. Project and manifest are at most64 KiB each,
mechanics16 KiB, model16 KiB, each text64 KiB and each WAV22,094B. Decoder output
is independently bounded. These are input/decode budgets, not a hard process-RSS
or peak allocation guarantee.

The writer emits each header explicitly, with no filesystem timestamps, OS
attributes, compression decisions or enumeration-order dependency. Identical
validated source bytes therefore have one output under this format. Original
text resources are pinned to LF in Git. Actual cross-platform execution remains
a CW-16 qualification item; same-host desktop/server byte equality is measured.

## Identity and ownership

Four hashes have separate purposes:

| Identity | Definition |
|---|---|
| Mechanics | Existing SHA256 of `WeaponMechanics.ToCanonicalBytes()`; unchanged CWM1 semantics |
| Dependencies | SHA256 of the canonical versioned empty lock |
| Package content | SHA256 of ASCII `WPD1` +NUL + canonical normalized manifest bytes |
| Archive | SHA256 of every exact ZIP byte, held outside the archive |

The manifest has exact `schema`, `kind`, `contentId`, `mechanicsHash`, `resources`.
Its sorted rows contain `path`, `mediaType`, `length`, `sha256`, excluding the
manifest itself. Intake verifies all rows against actual raw resource bytes,
with exact set equality and role media types. The content-hash manifest instead
normalizes project, mechanics and empty-lock JSON using their version1 writers;
other asset bytes stay exact. Thus metadata can change content/archive identity
without changing mechanics, and equivalent project/mechanics/lock JSON formatting
can retain content identity while changing the archive. No hash contains itself.
The published golden vectors pin this encoding; incompatible changes must retain
historical readers or version the format rather than silently changing identity.

Every returned archive/resource array is a detached copy. Project collections
are read-only, definitions use the existing immutable domain, and no package
chooses a native ID or registry runtime slot. Runtime IDs still come from the
frozen registry. All ZIP/JSON/model/audio work is explicit CPU tooling work;
normal gameplay does not call this library on ticks, draws or audio callbacks.

See [evidence](evidence/CW-11a.md) and [reference vectors](evidence/CW-11a-golden.json).
Android testing is deferred under the user's current instruction.
