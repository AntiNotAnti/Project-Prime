# CW-11b local store contract

Base: main `10297d8cd` (wire protocol 79). No wire, replay, native weapon,
inventory, world-contact, match-admission or feature-default changes.
The previous macOS checkpoint and draft test suite were unavailable on this
Windows checkout; implementation and tests are reconstructed from the user's
explicit constraints and the CW-11a public package contract.

`WeaponPackageStore` has no gameplay side effects. Construction and missing-store
listing do not create directories. Installation validates a detached complete
archive before creating store state. Exact historical revisions coexist; callers
identify revisions by lowercase archive SHA-256, never by display name or newest
logical ID. See [commands](11b-cli.md) for explicit CPU-only authoring operations.

The private user-owned store contains:

```text
.store.lock                             persistent exclusive cross-process gate
archives/<archive-sha256>.ppweapon       immutable, never overwritten
installed/<archive-sha256>.installed.json canonical installed marker
archives/.cw11b-<random-uuid-N>.tmp       private archive staging
installed/.cw11b-<random-uuid-N>.tmp      private marker staging
```

All operations acquire the same OS gate. The gate file is never deleted.
Acquisition opens a shared read-only archive handle while holding the gate,
validates its actual complete bytes and filename identity, and retains that
handle in `WeaponPackageLease` after releasing the gate. Prune requests exclusive
access while holding the gate; active leases in any cooperating process prevent
deletion. Disposal or process exit releases OS leases. Uninstall removes only
the exact validated marker; existing leases and historical archive bytes survive.
Explicit prune removes only recognized unmarked archives and recognized abandoned
staging files. Operator files and unknown names remain untouched.

Install writes and flushes each private stage in its target directory. Archive
promotion is non-overwriting. The final non-overwriting marker rename is the
commit point; all cancellation and fault boundaries precede it. No cancellation,
rollback or fallible result processing follows publication. Precommit failures
may leave a complete uninstalled archive, recoverable by reinstall or prune.
This is atomic visibility with flushed file contents; directory metadata is not
fsynced, so sudden power loss may require reinstall. No power-loss durability
claim is made.

Default quotas are 256 cached revisions, 128 installed revisions, 512 MiB total
archive/staging bytes, 4,096 entries per managed directory and a ten-second gate
wait. Options can lower limits (with hard upper bounds); no implicit eviction.
Package input remains bounded by CW-11a's 8 MiB archive/16 MiB expanded limits.
Known corrupt markers or referenced archives fail closed. Paths reject links,
reparse points, Windows devices, alternate streams and network/device roots.
The directory must be private to its user: managed path checks do not defend
against a hostile process with permission to rename ancestors concurrently.

Rollback: stop using `-weapon`; ordinary native sessions never consult the store.
Uninstall and prune are explicit. Installation does not grant/equip weapons or
change custom availability. Scene package activation is CW-11c; network package
readiness and replay binding are CW-12. Android testing remains deferred.
