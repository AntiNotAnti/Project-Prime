Run `dotnet run --project tools/map-publication-check -c Release`.

This asset-free regression links the production `MapRuntimeUsage`, `MapPublicationLease`
and `MapFilePublication` implementations. Separate reader and installer processes prove
that preparation and scene leases defer replacement of a rebuilt same-name immutable
package, preserve every existing runtime/package byte, release normally or after a
crash, and allow a retry to publish exactly the requested package/output hashes.
Separate scoped private historical scene/preparation readers retain their own bytes
while a same-name game room publishes independently. Those immutable private readers
belong to replay cache lifetime pins, rather than the active game installation fence.

The small game type fixtures isolate the ownership policy from game assets and graphics.
The fixture package contains deterministic runtime payloads; actual game package parsing,
compilation and model decoding remain covered by `map-editor-check --runtime-only`.
Lock files deliberately persist to prevent old-inode/new-inode ownership races.

Physical containment checks also link the shared BCL directory-alias resolver. They
preserve path casing and app bundle roles while the existing cooperative installation
identities retain their platform casing rules. Real ancestor/escape links, missing
destinations and alias cycles are covered; distinct case-variant sibling escapes are
exercised whenever the test filesystem supports them.

The same executable source-links `StudioPrivateMapRuntime` for private destination
ownership checks. A narrow build-call recorder proves rejected roots never cross the
publication boundary; it does not replace actual compiler/publication tests. The guard
rejects equal/ancestor/descendant game roots (including a filesystem root), physical game
aliases, swapped runtime and destination links, and distinct case-sensitive sibling
escapes. An initial runtime alias to the filesystem root remains rejected with no game
data configured, and cancellation reaches no publisher.
