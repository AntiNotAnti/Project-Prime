# CW-11a evidence — strict package foundation

Scope: library-only project/package parser, deterministic builder, original M7
reference exporter and CPU diagnostics. [Format](../11a-package-format.md).
Initial base main e117f6b56 (CW-10e/PR921). Installation, CLI, scene activation,
Community/Studio and package network/replay support are subsequent work.
No native weapon table, inventory, world pickup or existing transport changes.

Environment: SDK10.0.401/runtime10.0.12, macOS arm64, DOTNET_PROCESSOR_COUNT2,
single MSBuild worker, shared compilation off. Empty isolated user-data/cwd,
no extracted game files, GL context, audio device or network. Raw commands,
logs, before/after source/copy/DLL hashes and initial failures remain under
`artifacts/custom-weapons/CW-11a/`. Android validation is deferred per user.

| Check | Measured result |
|---|---|
| Initial desktop Release | PASS72.238s, one pre-existing DiscardPickupAcks CS0649 warning, zero errors |
| Initial actual package suite | PASS996, reference equals independent stored-ZIP writer |
| Diagnostic regression before fix | FAIL998 with exactly2 new failures: rejected unknown project/manifest fields leaked ESC/newline/bidi characters into exception text; prior996 pass |
| Final desktop candidate2 Release | PASS70.403s, same one warning/zero errors |
| Final references-only tool build | PASS3.397s, zero warnings/errors |
| Final package suite | PASS1,000/.543s process elapsed (.258s internal suite); full historical identities, both sanitizer regressions and prior negative suites pass |
| Full custom CPU | PASS35,726/5.652s |
| Fresh replay/cosmetics CLI | PASS3,050/.608s and16,226/.178s |
| Initial dedicated Release/probe | PASS48, exact full archive parity, no presentation/physics implementation assemblies loaded |
| Final dedicated candidate2 Release | PASS75.148s, one pre-existing warning/zero errors |
| Final dedicated probe build/run | PASS1.964s/zero warnings;50 checks/.283s, including both hostile diagnostics |
| Android | DEFERRED under user instruction; resource declarations updated, not compiled/tested in this slice |

Final candidate2 desktop SHA256:
`db5f9ab5d3748501c287afed9c058e3b71f10784283764e5e131997bfcac7068`.
Leaf DLL (unchanged executable code, revision metadata only):
`6e58fb9859f1c97cfc792868e52a2037e318bbce32b4cb63bbec34ddc791b3ab`.
Dedicated candidate2 SHA256:
`4f3009f8b9f3c3159aac7963d87dcc2033089736ada50bc246da857698c7ea8d`.
Diagnostic SHA256:
`bff1fd2190d3b36e747cc44e2970fec8e29e18a388fc263d428a641d21843177`.

The1,000-check suite exercises actual engine public APIs. An independent ZIP
writer creates structurally valid semantic-negative archives, so malformed
JSON/media/manifest cases reach those validators rather than only failing CRC.
Header/offset/length/count/CRC/SHA mutations separately test local/directory/EOCD
agreement, contiguity, ordering, overlap/gaps, symlinks/attributes, flags, methods,
encryption, descriptors, ZIP64 sentinels, trailing bytes and truncation.

Project/schema tests cover missing/unknown/duplicate fields at every supported
level, type errors, invalid UTF-8/surrogates, nonfinite values, number/depth/node
budgets, unsafe/case-aliased/reserved/device paths, length/depth limits, future
model/audio/mount/fallback capabilities and dependency URIs. Resource cases cover
exact set/role/hash/length agreement, missing/extra assets, executable/nested
payloads, malformed full model data, WAV header/payload/chunk/rate/channel/format
and0/11,025/11,026-frame boundaries.384 bounded byte mutations/truncations are
additional regression coverage, not an exhaustive security proof.

The reference archive is44,224B and identical to the independent writer, repeated
builds, reversed dictionary enumeration and tr-TR/fr-FR/ar-SA cultures. The
server's complete archive is byte-for-byte equal. Independent Python ZIP/SHA
calculation corroborates all four identities in the committed [golden vector](CW-11a-golden.json).
Tests pin those constants to protect historical version1 identity. Metadata,
mechanics and asset changes exercise separate digest effects; equivalent
project/mechanics/lock formatting preserves normalized content identity.

Actual source input/output mutations cannot alter retained package/project/
resources. Both native SP/MP nine-entry registries and full table snapshots stay
exact, registry assigns custom runtime10, CWM1 digest matches the existing M7,
and gameplay RNG remains unchanged. The reference exports the existing
RiftlineM7.Create mechanics, original exact model digest and deterministic integer
PCM generator, not a second combat implementation or native extracted assets.

Dedicated validation embeds only the original small reference resources for
explicit CPU export. Its deps/output exclude Core/BEPU; measured loaded
assemblies are ProjectPrime, the probe, OpenTK.Mathematics and framework support.
No GL/window/audio/UI implementation or physics assembly loads. This is actual
server CPU validation, not a graphical or authoritative-match run.

Read-only audit found the diagnostic-control issue; two tests independently
reproduced it before the bounded printable-ASCII fix. Remaining reviewed ZIP
range arithmetic, manifest joins, decode budgets, strict JSON, hash framing and
ownership produced no additional concrete blocking finding. This is scoped
evidence, not a general parser security certification. No general GLB/texture/
compressed-codec/dependency capability is advertised or accepted.

The library is unused by ordinary ticks/draws/audio callbacks and performs no
install or activation. No claim of zero-allocation package processing, target
device performance or completed create/publish/download/play flow is made.
Windows/Linux execution and all deferred Android acceptance remain CW-16 gates.
CI/review is not awaited or claimed under the user's immediate-merge instruction.

## Final latest-main qualification

Slicea28c4cdd69c305e0e92b2d75f101a4ccbeb8cf70 rebases cleanly onto latest fetched
main5b69db1f0 (PR923), preserving the unrelated PR922 UI and protocol77 Parkour
work, including its source-journal/replica changes. This slice changes no packets.
Final documentation amendment adds these receipts only.

Final desktop Release passes78.704s, one existing CS0649 warning/zero errors.
DLL `b4f11f70c4434c8080bd1db1e12aef7e228770c777f5001a53426b3e2f3e5516`;
leaf `89270b2cb1a94d0339c8fec8f341cd4031d17b99811d92c855b1e2a03da3a227`.
References-only tool build passes6.311s/zero warnings/errors; diagnostic
`7af4dcb526ae251868ebe51e3cb40094f8acd12499612d72696129f146e05a6e`.

Package1,000/.816s, current default CPU35,717/9.891s, replay format3,050/.784s,
cosmetics16,226/.306s and head-death replay3,278/1.315s all pass. The current
base changes the default CPU suite count; the earlier35,726 count remains
historical, not substituted for this run.158 source/resource/test hashes,
tracked-clean state and actual/copied DLL hashes stay stable through all phases.

Dedicated Release passes58.295s with the same existing warning; DLL
`4fd6358e01ac332c1217d5308bcc449f08ac5a6d3672e035225fa0839a673e4b`.
Public package50, full44,224B archive parity, absence of physics dependencies/
presentation implementation assembly loads and server replay format3,050 pass.
Fresh references-only nettest build10.665s/zero warnings; architecture/protocol17,
Parkour stance99/compatibility43, archive975, Quake compatibility664, Momentum126,
reliability, accepted343, Imperialist949 and registry77 all pass. No unexpected
failure remains. Historical package/content/dependency/mechanics vectors stay
identical on the rebased desktop and server.

Exact commands, environments, timings, raw results and source/copy guards are
under `CW-11a/rebased/` and `CW-11a/server-probe/rebased/`. No Android action was
run. The next slice adds CLI and immutable local installation; this foundation
still makes no install, acquisition, gameplay, Community or replay activation.
