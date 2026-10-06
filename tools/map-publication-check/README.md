Run `dotnet run --project tools/map-publication-check -c Release`.

This asset-free regression links the production `MapRuntimeUsage`, `MapPublicationLease`
and `MapFilePublication` implementations. Separate reader and installer processes prove
that preparation and scene leases defer replacement of a rebuilt same-name immutable
package, preserve every existing runtime/package byte, release normally or after a
crash, and allow a retry to publish exactly the requested package/output hashes.

The small game type fixtures isolate the ownership policy from game assets and graphics.
The fixture package contains deterministic runtime payloads; actual game package parsing,
compilation and model decoding remain covered by `map-editor-check --runtime-only`.
Lock files deliberately persist to prevent old-inode/new-inode ownership races.
