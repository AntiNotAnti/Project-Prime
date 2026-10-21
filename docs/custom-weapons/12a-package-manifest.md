# CW-12a — detached exact package manifest

CW-12a adds a bounded, versioned package identity contract and local lease binding.
It is a foundation for later match readiness and replay work. It does not send
network messages, make a peer Ready, activate a scene, grant a weapon, download
content, or enable custom replay. Existing `CWM1` mechanics manifests retain
their bytes, digest, runtime IDs and current callers.

The separate `CWPK` version 1 representation pins each mechanics identity to an
exact archive, canonical content identity and dependency identity. Runtime IDs
follow the existing mechanics manifest ordering. A package with matching damage
or the same content UUID cannot substitute for the requested archive revision.
`CustomWeaponPackageManifest` constructs this metadata from validated packages,
exposes detached canonical bytes through `CopyBytes`/`Write`, and accepts external
bytes through bounded `TryRead`. Its `Digest` is SHA-256 of the complete canonical
`CWPK` bytes; `MechanicsDigest` is the separately retained `CWM1` digest.

| Field | Encoding |
| --- | --- |
| Magic | Four ASCII bytes `CWPK` |
| Version | Unsigned 16-bit little endian, value 1 |
| Entry count | Unsigned 16-bit little endian, 1 through 64 |
| Mechanics manifest digest | 32 bytes, the existing `CWM1` digest |
| Rows | Exactly count × 146 bytes |

Each row consists of the runtime ID (2 bytes, little endian), content UUID
(16 bytes, big endian), and four 32-byte SHA-256 values in this order: mechanics,
archive, content, dependencies. The exact serialized length is 40 + count × 146
bytes; the largest manifest is 9,384 bytes. Canonical ordering and exact length
checks prevent alternate row ordering or trailing data from naming the same
manifest. Empty UUIDs, invalid digest sentinels and unsupported versions fail
closed.

`WeaponMatchPackageBinding.BindInstalled` requires the exact installed marker
and validated archive. `BindHistorical` can retain an exact cached archive after
uninstall. Both
paths verify every pinned identity against the validated package and retain
store leases; neither searches for a newer revision or falls back to the bundled
M7. Binding owns no graphics or gameplay resources. Failed or cancelled binding
must release acquired leases; successful binding retains them until disposal.
Uninstall can remove an installed marker while existing leases remain usable;
prune preserves leased archives until their final owner releases them.
Bindings expose their frozen `Manifest`, package `Count`, indexed `Package`,
and runtime-ID-based `Definition`. Disposal is idempotent; package/definition
access through a disposed binding is rejected. Store `Acquire` and
`AcquireInstalled` retain their original overloads and add cancellation-token
overloads so binding can cancel while waiting for a store gate. Binding checks
cancellation before each acquisition and after the last, releasing partial
ownership on failure. Individual bounded archive validation is synchronous.

The store remains private local storage. This contract supplies no remote trust,
creator authorization, publication policy or dependency downloading. Schema 1
packages remain self-contained. Tests and evidence receipts must distinguish
these detached identity/lifetime checks from live network readiness, package
activation, custom replay and end-to-end CW-12 acceptance. Android validation
remains deferred by user scope.
