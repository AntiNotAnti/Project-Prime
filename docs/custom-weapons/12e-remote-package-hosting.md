# CW-12e — remote package hosting contract

This describes the current source contract; validation is pending. It makes no
test-pass, launcher UI or complete CW-12 acceptance claim.

Protocol83 appends32 raw SHA-256 bytes to `HostRequest`. All-zero bytes mean no
package selection. A request carries an archive hash only, never the operator's
filesystem path. CWM1, native Intent/snapshot payloads and replay checkpoint14
remain unchanged; historical protocol82 layouts remain supported explicitly.

An operator can supply `-hostweaponstore <directory>` to `-server` or
`-masterserver`. The default is no approved package store. Requests can select
only explicitly installed, validated exact archive bytes in that approved
local store. No Community weapon download, implicit installation, newer-revision
substitution or creator-supplied remote path is accepted.

Package preparation uses the existing bounded hosting request coordinator:
four workers and32 retained requests. Request-wire deduplication precedes
preparation and child allocation. Hash validation and installed acquisition
complete before port selection or process creation. A package-only request
skips custom-map cache/download work; a request requiring maps and a package
retains each prepared dependency through its corresponding existing path.

`PreparedHostRequest` owns optional prepared maps and a
`PreparedLocalWeaponPackage` lease. Completion is handed to the packet-loop
owner. Its Pump releases the preparation proof in `finally` after the launch
attempt, including failure. A cached reply retains no active preparation lease.
Disposal cancels unfinished work; a continuation disposes any prepared owner
that finishes after cancellation or coordinator retirement.

`HostPool.Start` refuses direct package selection without a prepared proof,
before allocating a port. The prepared path verifies the proof against the
currently approved operator store and requested archive hash, then retains an
active launch owner. `LocalServer.StartPrepared` consumes that proof without
archive reparsing or store I/O on the packet loop. Operator approval changes
cannot silently reuse a proof for another store or revision.

The child receives the approved local store and exact archive as process
arguments and independently calls `AcquireInstalled`. If uninstall removes the
marker before child acquisition, startup fails closed even if parent leases
still preserve the bytes. A successfully prepared child holds its own binding;
parent proof disposal cannot release the child's ownership. Package hosting
forces a persistent lobby and forwards its owner token to the requesting
ordinary client. Neither allocation nor readiness grants or equips the weapon.

`NetMasterClient.RequestGame` appends an optional archive-hash argument. The
terminal caller selects a local client revision with paired options:

```text
ProjectPrime -hostgame "MP3 PROVING GROUND" -weaponstore ./weapons -weaponarchive <exact-lowercase-package-sha256> -master <allocator>
```

`-hostgame` validates and preflights the client's installed local selection.
Only its archive hash crosses the allocator request. The reply's owner token
and the original connection-bound client selection reach `NetLaunch.Join`.
The client's path is not transmitted, and allocator configuration does not
replace the client's store. Matching installed bytes are independently required
on both machines; full package/native-world readiness and ordinary world pickup
remain necessary after allocation. No download, installation or ownership grant
is implied by a successful request or Join.

This slice adds allocator selection to the existing exact single-package path;
it does not implement custom bots, custom replay/bootstrap/seek or the
Studio/Community UI workflows in CW-13 through CW-15. Native replay continues
refusing package/custom gameplay. Android testing is deferred by user scope;
CI is unobserved. Worker bounds and ownership rules are source properties,
not performance measurements. Integration, cancellation, port/process cleanup
and ordinary client gameplay require separate observed qualification.
