# CW-12a — exact package manifest and retained binding

Base main `8b8c9b12141bd66f2c2245b78fcbae0af39d5bb5`; protocol 79.
Windows x64, .NET SDK 10.0.401/runtime 10.0.12, two .NET processors,
one MSBuild worker and shared compilation disabled. Main assemblies were
forcibly rebuilt before references-only tool builds; copied assembly hashes
were compared before testing. No product source changed during compilation.

[CWPK version 1](../12a-package-manifest.md) pins each unchanged CWM1 identity
to exact archive, content and dependency digests. Parsing is bounded to 64 rows
and 9,384 bytes. Installed bindings require the exact installed marker;
historical bindings may retain the exact uninstalled cached revision. All
identities are checked against fully validated bytes. Partial failure and
cancellation release acquired leases; successful bindings prevent pruning
until disposal. No native IDs, CWM1 bytes, network packets or gameplay callers
change. These APIs neither install content nor grant/equip weapons.

| Local check | Observed result | Raw evidence |
| --- | --- | --- |
| Forced desktop Release | PASS 161.22s; existing CS0649 warning only | `desktop-build.log` |
| Forced dedicated Release | PASS 71.20s; same warning only | `server-build.log` |
| Desktop manifest/binding | PASS 657; 0.6362s | `desktop/manifest.log` |
| Actual dedicated manifest/binding | PASS 657; process exit 0 | `server-probe/manifest.log` |
| Store/CLI regression | PASS 99 | `desktop/store.log` |
| Historical package/parser regression | PASS 1,000 | `desktop/package.log` |
| Custom CPU/native isolation | PASS 36,965 | `desktop/default.log` |
| Native replay formats | PASS 3,050 | `replay.log` |

Raw evidence is retained under `artifacts/custom-weapons/CW-12a/`.
The dedicated probe loads no Avalonia, BEPU or CorpsePhysics assemblies.
Desktop assembly SHA-256:
`da06e1dfe297780523ceac2896ce7992ec29cd5b69a93c6b71ac8bc239764cc1`.
Dedicated assembly SHA-256:
`d9b25298cdffeb83ebd2b24c8ab1d7b416e8dcade9f415b5fa21a6b7ad1bc133`.

The new suite independently writes reference bytes, pins CWM1 digest
`a0bc00be2884eb5b4f188f6453cc452060ccf920e26af610f419d0352709b6e9`
and CWPK digest
`e58d66d03426c22db5b414ecddb11d33f4bdb8b4b19a8800563d372ba3cda7a4`,
tests all truncations of the one-row encoding, invalid counts/versions/order/
duplicates/IDs/digests, the 64/65 row boundary, 400 bounded random inputs,
immutable views, metadata-only revision changes, identity mismatches, real
lease retention, partial failure, historical refusal and gate-wait cancellation.

Performance evidence is a bounded contract-test duration, not a gameplay frame
benchmark; no gameplay path invokes this foundation yet. Existing native replay
tests remain applicable because custom replay refusal is unchanged. Live lobby
admission, allocation, late join/reconnect, package-backed online pickup/runtime,
bots and custom replay/seek remain later CW-12 work. This slice cannot make a
peer Ready and is not full CW-12 acceptance. Android builds/emulators/devices
remain deferred; CI was neither awaited nor reported passing. Rollback removes
unused CWPK/binding APIs and their tests; no persisted store migration exists.
