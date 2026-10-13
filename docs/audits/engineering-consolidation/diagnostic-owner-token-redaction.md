# Diagnostic command-line owner-token redaction

Baseline `91b1c0c6c44e8c90effd250729709f791a84765e`; branch `codex/consolidation-diagnostic-token-redaction`. Phase 0 #489 evidence baseline. No code dependency; separate from other diagnostics/native attribution changes.

## Purpose and source evidence
`LocalServer.Start` supplies the child `-ownertoken` argument (`LocalServer.cs:466–467`). `ModEntry` obtains its value with `ValueAfter` and `Guid.TryParse` (`ModEntry.cs:1084–1088`, `2685–2695`). `LobbyCommands.ClaimOwner` authenticates the joining process owner against that token (`LobbyCommands.cs:149–153`). `DebugLog.WriteHeader` formerly joined all process arguments into its args header (`DebugLog.cs:369`). That header is retained in the crash-report ring even with persistence disabled and is later copied into persistent logs by Attach.

The header now replaces owner-token values with `(redacted)` before entering either sink. Match the actual CLI parser: ordinal-insensitive `ownertoken`, with zero or any number of leading hyphens, and a separate following value. Every occurrence is redacted, including invalid values. Existing CLI does not accept `-ownertoken=value`; no new syntax is introduced. Executable and nonsensitive arguments remain useful, and the caller's argument array is unchanged. Authentication/parser/server behavior is unchanged.

## Regression evidence
Source-linked actual `DebugLog.cs` with inert metadata stubs; uniquely owned synthetic files and sentinel GUIDs only. The test exercises the exact args-header method, actual `Line`, persistent writer and `AppendRecent` crash ring. It injects a private StreamWriter to avoid Attach (which redirects native stderr); it never reads user preferences/tokens or starts native/game code. All failure output is generic and never prints sentinel values.

- Baseline header behavior factored into the same method: **2 PASS / 22 FAIL**, synthetic credentials retained in both sinks.
- Candidate: **24 PASS / 0 FAIL**. Covers parser spellings, five Guid formats, repeats, invalid values, missing values, unrelated prefix options, preserved nonsensitive arguments, unchanged input arrays, disk and crash-ring sinks.
- CI runs the source-linked device-free tool in the assets/policy job with .NET SDK setup.
- Desktop Release compilation: PASS, 30 warnings in unchanged source / 0 errors. Workflow YAML and diff checks PASS.

```sh
/Users/jarrett/.dotnet/dotnet run --project tools/diagnostic-args-check -c Release
/Users/jarrett/.dotnet/dotnet build src/MphRead/MphRead.csproj -c Release --nologo
git diff --check
```

## Scope, risk and rollback
This closes the demonstrated args-header disclosure only. Other log messages, filenames, native stderr and crash dumps are not certified secret-free. Ring count (2,048) and log retention (eight files) remain unchanged; individual line/current-file byte limits remain a separate disposition. No broad security, network authentication improvement, native/RSS or physical-platform claim.

Physical child-server authentication and platform stderr capture: NOT_RUN (unchanged paths). Revert this commit to roll back; no settings or schema migration. Independent DebugLog changes must preserve this header redaction during integration.
