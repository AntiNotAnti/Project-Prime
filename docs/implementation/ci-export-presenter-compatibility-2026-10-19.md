# Export presenter CI compatibility — 2026-10-19

PR 730's `no game assets` job failed in `RmlUi gameplay and HUD controller contracts` when compiling the source-linked `rmlui-export-check` harness. The production export presenter now reads the encoder executable field before enqueue and initializes it when opening the modal. The harness's native-host substitute lacked `ReadField` and `SetField`; the production host already implements both.

The test-only host now implements the existing production signatures, document-scoped field storage, ignored retired-document writes, empty retired-document reads, and the same 1..131072-byte UTF-8 read limit. The source-linked presenter test edits the actual modal field, verifies refresh preserves the draft, then verifies the reviewed executable reaches the injected export backend. Focused assertions cover document isolation, retired documents, and exact UTF-8 byte limits. Production UI and export code are unchanged.

Validation: `/Users/jarrett/.dotnet/dotnet run --project tools/rmlui-export-check -c Release` passed 19 existing export-controller checks and 62 source-linked presenter/asset/intent checks. This project compiles linked source independently and requires neither game assets nor a core build. `git diff --check` passed. The full hosted CI job must still run on the published compatibility change.
