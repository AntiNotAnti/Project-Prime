# Native Social page check

This source-linked executable tests the immutable Social controller and the actual RmlUi page with an injected service backend. It never performs authenticated requests or joins a live lobby.

```sh
dotnet run --project tools/rmlui-social-page-check
dotnet run --project tools/rmlui-social-page-check -- --native /absolute/path/libProjectPrime.RmlUi.Native.dylib
```

During an isolated service worktree build, pass `-p:MigrationRoot=/absolute/path/to/migration-checkout` to use the current managed host and shared RML components. After importing into the migration checkout, the relative default suffices.

The native mode tests real pointer and keyboard events, full-width scrollable layout, wheel access to privacy controls, all tabs, editable search/lookup drafts, paging, privacy persistence, destructive confirmation focus and stale state rejection, explicit join/admission handoff, cancelled work and document retirement at 1280×720@1, 2560×1440@2 and 2560×1440@1. Existing service integration and reservation proofs are checked separately by `tools/social-controller-check` and service contract checks.
