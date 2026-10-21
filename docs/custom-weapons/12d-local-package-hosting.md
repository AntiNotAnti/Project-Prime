# CW-12d — local package hosting contract

This is a source/design contract. Current CW-12d validation is pending; this
document does not establish test passes, UI delivery or complete CW-12 acceptance.
The initial112.53s desktop and70.84s dedicated builds were superseded before
functional testing when review found a missing joining-client selection path.
The corrected qualified rebuild is running; no functional pass is claimed here.

`LocalWeaponPackageSelection` is an immutable public value containing a bounded
local store directory and an exact lowercase64-character archive SHA-256.
Construction validates and normalizes the path without creating store state.
Its internal acquisition requires the installed marker and validated exact
archive. This is operator-owned local configuration, never a peer-supplied path.

`LocalServer.Start` appends an optional `weaponPackage` parameter. Under the
existing `StartLifecycleGate`, cancellation and installed-package preflight
precede port selection, rotation writes and process creation. Package hosting
requires a bounded Battle rotation. The parent holds its acquired lease across
launch/readiness and releases that launch owner when `Start` returns. It does
not transfer its file handle or substitute an in-process simulation for the
dedicated child.

The child receives the selected store and archive through separate
`ProcessStartInfo.ArgumentList` entries and independently calls
`AcquireInstalled`. Package selection forces a persistent lobby with an owner
token. Omitting the selection preserves ordinary native arguments and defaults.
No download, installation, grant or automatic package substitution occurs.

Uninstall before the child acquires its installed marker fails closed, even
while the parent lease still preserves the archive bytes. Once the child has
successfully acquired the revision and is online, its independent binding
survives parent launch-lease release and subsequent uninstall. Prune must retain
the archive until its final child/session/scene owner releases it. Cancellation
and failed launch follow the existing startup cleanup contract.

`NetHostSession.StartAndJoin` appends the same optional selection. It captures
the running process and owner token, then detaches that process from
`LocalServer` atomically under `StartLifecycleGate`. `NetLaunch.Join` receives
both the captured owner token and package selection, preserving authenticated
lobby ownership and the joining client's exact local archive requirement.
A successful connection still waits for lobby Start and the
existing package/native-world readiness barriers; hosting does not grant a
weapon or make the joining player combat authority.

`NetLaunch.Connect`, `NetLaunch.Join` and `NetSession.StartClient` accept the
optional selection. Its store directory and expected archive are bound to that
client connection without mutating the global environment. The override survives
match-preparation resets but clears on full Stop/reconnect; a new connection
must receive its own selection. Preparation captures the store directory before
`Task.Run`, so queued work cannot read a later connection's mutable path.
A mismatched server offer sets an error and sends no Ready. Connecting with an
explicit selection to a native-only server refuses, and queued admission cannot
be combined with explicit selection.

The terminal path accepts the paired local package options:

```text
ProjectPrime -hostlocal "MP3 PROVING GROUND" -weaponstore ./weapons -weaponarchive <exact-lowercase-package-sha256> -nomaster
```

`-hostlocal` and the dedicated `-server` path share argument validation. Both
options must appear once with valid values. Invalid pairs return exit2 early
with a fixed bounded diagnostic. The terminal Join path still waits for lobby
Start; the flags select an available world-pickup weapon, never spawn ownership.

This slice introduces no remote allocator wire change and makes no launcher UI
claim. Remote allocator selection, custom bots and custom replay/seek remain
open. Native replay continues explicitly refusing package/custom gameplay.
Android testing remains deferred by user scope. Local child lifecycle, exact
argument forwarding, ownership and gameplay observations require separate
validation evidence before this source contract can be called qualified.
