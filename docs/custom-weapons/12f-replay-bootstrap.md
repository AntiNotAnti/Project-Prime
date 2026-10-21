# CW-12f — detached historical package bootstrap

This initial replay slice defines a file-only bootstrap and historical resource
preparation. It does not yet integrate the bootstrap into a demo archive or
enable custom recording, playback, killcams, clips or export. Native archive
formats3–6, replica checkpoint14, world checkpoint11 and live protocol83 are
unchanged. Their existing custom-gameplay refusal remains active.

`ReplayCustomWeaponBootstrap` is an immutable schema1 value with these exact
little-endian fields. Digests retain their existing raw32-byte conventions.

| Offset | Bytes | Field |
| --- | --- | --- |
| 0 | 4 | `CWRB` |
| 4 | 2 | Schema1 |
| 6 | 2 | Required capability `ExactPackages` =1 |
| 8 | 32 | Complete existing `WorldBootstrapIdentity` |
| 40 | 8 | Nonzero package configuration identity |
| 48 | 32 | Nonzero frozen pickup-layout digest |
| 80 | 32 | CWM1 mechanics digest |
| 112 | 32 | Complete canonical CWPK digest |
| 144 | 2 | CWPK byte length |
| 146 | Length | Complete canonical CWPK schema1 bytes |

The parser bounds the entire value to16KiB before reading fields. It rejects
unknown schemas/capabilities, zero required identities, malformed world/start
identity, incorrect lengths, trailing bytes, invalid canonical package metadata
and mismatched CWM1/CWPK digests. The current runtime admits exactly one package
at runtime ID10; broader manifests are refused here even though detached CWPK
itself supports64 entries. No local paths, URLs, executable code, assets or
private acquisition knowledge appear in this bootstrap. The reference value
occupies332 bytes. Returned byte copies cannot modify the retained contract.

Historical preparation uses `WeaponMatchPackageBinding.PrepareHistorical`.
It acquires exact cached archive bytes without requiring an installed marker,
verifies every recorded mechanics/content/dependency/archive identity and
prepares immutable CPU model/PCM resources before transferring ownership.
Failure/cancellation disposes acquired leases; GL/audio-device ownership stays
outside the worker. Retained bindings share the prepared immutable resources
and independently retain the exact archive. Uninstall cannot remove bytes from
an open replay owner; pruning is possible after the last owner closes.

Missing/corrupt historical bytes produce an explicit unavailable/invalid exact
revision error. No current-revision substitution, bundled fallback, implicit
installation, network download or gameplay grant occurs. A future archive
reader must bind this bootstrap to its recorded world, custom facts and
checkpoints before constructing a passive scene. Parsing/preparing this object
alone proves no accepted gameplay or replay reconstruction.

Remaining CW-12 work must record accepted ownership/resources/pickups/launches
and outcomes, reconstruct passive state and flights, version durable custom
archive/checkpoint schemas, and demonstrate genuine record-close-reopen plus
backward/forward seeks before opening the recording gate. The custom bot gate
also remains open. Android testing is deferred by user scope.
