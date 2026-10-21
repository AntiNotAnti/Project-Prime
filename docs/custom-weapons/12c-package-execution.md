# CW-12c — exact single-package execution

This document describes the current source implementation. It is not a
validation receipt, release qualification, or declaration that CW-12 is complete.
The live package path uses protocol81. Native packet layouts, native weapon IDs,
the existing CWM1 encoding and historical Parkour profile attribution remain
unchanged. CWPK continues to pin archive, content, dependency and mechanics
identities separately.

The dedicated-server command accepts one explicitly installed package:

```text
ProjectPrimeServer -server -weaponstore ./weapons -weaponarchive <exact-lowercase-package-sha256> -nomaster
```

Supply `-weaponstore` and `-weaponarchive` together, once each. The archive
argument is the 64-character lowercase SHA-256 of the exact `.ppweapon` archive,
not its mechanics digest or content UUID. Invalid arguments and unavailable or
invalid installed revisions fail with bounded ASCII diagnostics. Configuration
runs before server startup and forces a persistent lobby. The server still
requires the operator's game files and normal room/rotation configuration.
Neither flag downloads, installs, publishes, equips or grants a weapon.

Clients must explicitly install the same archive before joining. Their package
store defaults to `weapons/` under the user data directory;
`PROJECT_PRIME_WEAPON_STORE` can select a different private local store. The
operator-selected server binding remains immutable while the server runs.
The live runtime accepts exactly one package even though detached CWPK manifests
can describe up to64 definitions.

Package preparation and ordinary lobby Ready remain separate gates. The client
worker acquires and validates the exact installed archive, then prepares its
bounded model and four decoded PCM cues as detached CPU resources before
acknowledging preparation. The worker creates no graphics or audio-device
objects. A global worker fence prevents overlapping package parsers across
cancellation and reconnect. Scene binding retains an independent owner of the
verified archive lease and prepared resources; it performs no package disk read
or decode during simulation ticks. Audio-device recreation reuses cached PCM.
Scene teardown and connection teardown can release their owners independently.
Uninstall removes the installation marker without invalidating an already
retained binding, and prune preserves an archive until its final lease releases.

The selected package definition flows into the authority combat adapter,
post-load CWM1 readiness, pickup layout and recipient mechanics. CWM1 is still
the mechanics/source-readiness contract; it does not replace the earlier exact
archive preparation gate. The ordinary client binds its scene resources to the
current match start and CWPK digest before reporting the world loaded. Package
mechanics provide magazine, reserve, reload, firing and optic parameters. The
precision schema supports one through four ordered optic magnifications; input
cycling follows the selected definition rather than assuming two optics.

Players begin each life without custom ownership. They must reach the authored
world pickup through the ordinary authoritative acquisition path. Package
selection and both readiness handshakes provide availability, never a spawn or
menu grant. The selected definition uses the same bounded pickup, funding,
accepted-shot and replica paths as the built-in custom weapon. The existing
stock pickup locations remain authored locations; the package does not supply
an arbitrary map or a second combat authority.

Preparation keys bind match ID, authority epoch, operator configuration, slot,
slot generation, transport connection incarnation and CWPK digest. Start can
announce a newer match ID before its package offer arrives. During that gap,
the client retains the previous prepared owner and keeps the new match gated.
It carries ownership into the new key only when epoch, configuration, recipient,
generation, connection and manifest still match; it acknowledges the new key
and waits for its confirmation. Reusing the bytes does not reuse an old Ready
acknowledgement. Disconnect or a replacement connection/occupant requires fresh
preparation; old keys cannot prepare the replacement. Scene ownership is also
bound to the current match-start identity, independently of the retained store
lease.

Native replay recording, killcams and archived reconstruction explicitly refuse
package/custom gameplay. A package availability bit alone cannot enter native
replay by omitting the built-in custom availability bit. This slice does not
claim custom replay or seek, Community distribution, Studio authoring, a host
allocator package-selection flow, multiple simultaneous custom weapons, or
full CW-12 acceptance. Desktop/server evidence must be recorded separately.
Android testing remains deferred by user scope.
