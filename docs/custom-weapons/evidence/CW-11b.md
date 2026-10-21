# CW-11b — immutable local store and CPU commands

Base `10297d8cd` (latest fetched main, protocol 79). Windows x64; pinned
SDK 10.0.401, `DOTNET_PROCESSOR_COUNT=2`, one MSBuild worker, shared compilation
disabled. The SDK was located in an existing local tool directory and used
read-only. Earlier bootstrap builds used explicitly selected SDK 10.0.400;
those results are historical development runs, not final qualification.
The old macOS checkpoint/draft was unavailable on this host. No unrelated
checkout, stash, emulator, Android build or device was changed.

Scope: [store contract](../11b-local-store.md), [CLI](../11b-cli.md).
No native enums/tables, simulation, pickup synchronization, protocol bodies,
replay schemas or feature defaults changed. Installation never grants/equips.

| Final local check | Observed result |
| --- | --- |
| Desktop Release | PASS 105.38 seconds; one existing CS0649 warning, zero errors |
| References-only custom checker | PASS 3.17 seconds, zero warnings/errors |
| New real store/CLI suite | PASS 99 checks, 2.346 seconds |
| Historical package vectors/parser | PASS 1,000 checks |
| Existing custom weapon CPU suite | PASS 36,962 checks |
| Desktop replay format | PASS 3,050 checks |
| Desktop cosmetics | PASS 16,226 checks |
| Dedicated Release | PASS 97.07 seconds; same existing warning, zero errors |
| Direct-reference dedicated probe | PASS 99 store/CLI checks; copied server DLL hash matches |
| Actual server command dispatch | PASS export/build/install/list, missing-input exit 1, historical exact archive hash |
| Dedicated replay format | PASS 3,050 checks |
| Linux/macOS execution | Not run in this Windows slice; CW-16 qualification remains open |
| Android | Deferred by user; no builds/emulators/devices run |

Final desktop DLL SHA-256:
`da9d364880d92cb1bac5686e4148ee7037542b285707f8aefe66a2c33953abc2`.
Checker verifies its copied game DLL equals the freshly built source DLL before
each run. Logs, raw JSON, source guards and build attempts are retained locally
under `artifacts/custom-weapons/CW-11b/`; no private game assets are committed.
The dedicated probe reuses the same suite against the actual server assembly;
its loaded-assembly inventory contains no Avalonia, BEPU or CorpsePhysics.

Store checks use actual shared OS file handles in independent processes, including
two concurrent readers, prune while leased, release in either order, process kill,
gate contention and gate-owner kill, and simultaneous independent installers.
They cover no state for invalid/cancelled intake, coexisting historical revisions,
corrupt marker/archive refusal, immutable filename hashes, all precommit fault
checkpoints, cancellation immediately before promotion, and cancellation/observer
failure after publication without rollback or reported failure. Quota checks cover
byte/count limits, explicit recovery and reopening under lower admission quotas.

The actual CLI APIs export/editable-build to the historical exact 44,224-byte
reference, validate/install/list/uninstall/prune, refuse output replacement and
reject malformed/missing sources and unsafe paths. Root/archives/installed
directory-junction refusal ran with an unchanged target sentinel. File symlink
creation lacked OS privilege and is explicitly skipped, not claimed as tested.
Unknown operator files survive pruning; only recognized abandoned stages are
scavenged. Managed path checks assume private user-owned directories and do not
claim protection against a hostile process renaming writable ancestors.

An initial new suite failed on CLI malformed-input handling because
`InvalidDataException` is not an `IOException`; the final CLI catches it explicitly.
Review also fixed stage cleanup ownership, raw Windows trailing-dot/space aliases,
and prune recovery when configured admission quotas shrink. These failures and
development builds remain in the local logs. No failed check was disabled.

Atomic marker visibility is validated; sudden-power-loss directory durability,
antivirus/storage-failure exhaustiveness, Linux/macOS lease behavior and graphical
activation are not established here. CW-11c must demonstrate an installed package
through real rendered world pickup gameplay. CI/review is neither awaited nor
reported passing under the user's immediate-merge instruction.
