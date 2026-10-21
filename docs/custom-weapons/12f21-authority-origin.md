# CW-12f21 — detached authority replay origin

This foundation distinguishes an authority origin from a recipient bootstrap.
[Local qualification evidence](evidence/CW-12f21.md) covers the detached metadata
foundation. It does not admit actual
server recording.

`ReplayCustomWeaponBootstrap.ForAuthority` writes CWRB version2 with an actual
match/start identity and nonzero authority frame. It carries no recipient slot
generation, slow revision or Ready ACK. Existing recipient construction remains
version1 with unchanged bytes. Both origins expose common `Start` and
`AuthorityFrame`; nullable `RecipientBootstrap` identifies recipient-only data.
Accessing `World` for an authority origin throws instead of inventing a recipient.

The146-byte header and16KiB total bound remain unchanged. Version2 uses byte8
origin tag2, zero bytes9–11, match ID at12, epoch at14, start generation at22 and
authority frame at26, then zero bytes30–39. Package configuration remains at40,
layout digest48, mechanics digest80, package-manifest digest112, manifest length144
and manifest bytes146. Integer fields retain little-endian encoding. Flags must
remain exactly `ExactPackages=1`; reserved bytes, invalid identity, wrong lengths,
unexpected runtime IDs or inconsistent digests are refused.

Pure custom replica validation shares the six frozen codec primitives. The live
readiness wire caches its validation context once at construction; this does not
turn authority data into a readiness carrier. Client capture and Scene package
retention explicitly require recipient origin, preserving their existing trust
boundary. Exact historical package binding still validates every recorded identity
and owns retained immutable resources; absence never installs or falls back.

Protocol85, legacy recipient theatre/bundle behavior and world13 schemas remain
unchanged. A future server recorder still needs a trusted committed-fact feed
and independently retained server-prepared package. Detached authority parsing
alone grants no inventory, accepts no network packet and opens no recording gate.
The next server capture contract must define late same-frame fact phases at
every durable checkpoint, not just the initial frame0 boundary; such a contract
is a future requirement, not part of this qualification.
Full CW-12, bots, killcam/export and other deferred admissions remain open.
Android testing is deferred; CI and performance qualification are not claimed.
