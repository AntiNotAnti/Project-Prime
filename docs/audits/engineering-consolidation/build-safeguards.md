# Bounded network scratch and precise application-image identity

Main baseline: `91b1c0c6c44e8c90effd250729709f791a84765e`; .NET SDK
10.0.401/runtime 10.0.12, macOS 27 ARM64. This slice repairs warning causes
and adds narrow build guards. It preserves protocol 53, replay schemas,
settings, default headless simulation, GL/GLES and the platform matrix.

## Changes and ownership

Four CA2014 sites reuse one stack span per enclosing method/service pass:
reliable datagram copies and acknowledgment headers in `NetTransport`, the
nonce retry in `NetMaster`, and accepted form-source evidence in
`NetCampaignEvidence`. Every dispatched header and payload byte is rewritten
before use; dispatch ownership and delayed-copy lifetimes remain unchanged.
The nullable connection local reflects TryGetValue's actual contract.

`RuntimeApplicationImage` identifies the exact loaded assembly. A single-file
assembly's empty Location triggers an explicitly labeled process-image hash.
One documented IL3000 pragma surrounds only that intentional Location read;
other IL3000 and CA2014 diagnostics are errors on desktop/server and Android.
This avoids hashing an unrelated DLL beside a bundled host or guessing a path
when an assembly was loaded from another directory. Framework-dependent
settings restart uses the same exact assembly path after working-directory
changes. The bomb-pixel witness records whether its checksum names an assembly
or process image; its diagnostic schema is extended additively.

Server-only retained-renderer locals are conditionally compiled with their
uses. Artifact diagnostic logging uses the scene-owned RoomId already set
alongside Room.RoomId; it no longer dereferences a nullable room solely to log
that same identity. No combat, history or turret nullability policy is changed.
The obsolete Android XA0141 exemption is removed so native page-size warnings
are visible; no native binaries or packaging alignment are changed.

## Validation

| Check | Status | Evidence |
| --- | --- | --- |
| Final Desktop/nettest Release | PASS | 0 errors, 22 client warnings plus one pre-existing CS0105 tool warning |
| Final server-only Release | PASS | 0 errors, 11 warnings; separate artifact output avoids client contamination |
| Framework-dependent image identity | PASS | Six real-file assertions, including renamed external assembly and changed cwd |
| Actual single-file image identity | PASS | Six assertions; unrelated adjacent ProjectPrime.dll and matching-name DLL cannot replace the host identity |
| Network protocol and source-form fixtures | PASS | 37 Enhanced/Balanced budget checks; protocol18 maximum 1,172 bytes/zero decode allocation; 31 player-form checks |
| Reliable/transport/queue/campaign regressions | PASS | Seven registered modes overall, 1,728 impaired intents/128 exact-once controls, 14 campaign-claim assertions |
| Earlier Android Release/AOT candidate | PASS | 0 errors/64 warnings; predates final diagnostic RoomId substitution and exact current-source CI |
| Final-head platform CI | PENDING | Verify hosted final SHA before considering this PR complete |
| Physical Android/native-page-size and Windows/Linux/macOS package launch | NOT_RUN locally | Hosted platform gates and physical-device acceptance remain separate |

The SHA-bound manifest contains exact commands, build/assembly hashes, original
logs and regression receipts. Final warnings are CS8602 (three client/server
sites), CS0162 (11 desktop/eight server), CS0618 (eight desktop) and the existing
tool CS0105. The corresponding main counts were 30 client/22 server. This slice
reduces them to 22/11 without blanket suppression. UI owners separately address
obsolete calls and constant-false post-match branches. Remaining turret-related
nullability is recorded instead of masked; claim/history semantics overlap
open net21 work and need their own invariant-backed change.

## Build and package decisions

The Android evaluated source list has 1,127 items (1,097 shared) with no duplicate
compile identities or desktop-generated source paths. Preserve the current
GLES aliases, net10.0-android36.0, AOT/startup and conditional UI behavior. A
future resource-independent assembly could start with packed data/math,
protocol codecs and pure rule types after explicit dependency checks; this PR
does not move scene, player, UI or renderer ownership into a new assembly.

An earlier Linux-x64 server candidate publish contains a 148,167,932-byte
self-contained apphost and 1,580,488 bytes of loose GLFW/miniaudio/STB libraries.
Avalonia is absent. These are output-inventory measurements, not a final
package-size improvement or native-pruning claim. Common compiled commands can
still invoke them: `Program` -room/-model creates RenderWindow, legacy -menu
can initialize MusicPlayer, and layer2d/object2d export writes through STB.
Pruning those files before separating supported entry-point capabilities risks
breaking compiled tools. Default dedicated ServerSim startup remains headless.
No client dependency, license, platform gate or workflow job is removed.

Windows/Linux/macOS/Android builds, updater/rollback, signing, release archive
checksums, retail-asset guards and launch gates retain their existing CI paths.
Build/release jobs have different packaging/signing triggers; no workflow
consolidation is proposed without equivalent execution evidence. Keep the
repository's existing no-retail-data and license guard evidence in final
integration; an SDK/source build alone does not establish release readiness.

## Risks and rollback

Revert this focused PR to restore prior spans, diagnostic hashing/restart and
warning severity. Protocol and user-data migrations are unchanged. No runtime
speed, FPS or install-size improvement is claimed. Diagnostic source hashes
identify the image actually available, and do not substitute for archived
source provenance or physical platform acceptance.

Next gates are final-head CI, combined UI/warning/linker integration, complete
release packages and hardware-specific startup/page-size smoke. Native-memory
P0 acceptance remains independent of these warning repairs.
