# RML65 text-edit evidence

These files preserve root-executed private tests; this agent did not run builds
or desktop windows. Inputs were isolated synthetic text documents, existing
pinned dependencies and packaged asset clones. No user text, saves, accounts,
backend writes or game assets were accessed.

- `manifest.json`: frozen private source/dependency/asset/harness input hashes.
- `build-results.json`, `built-output-manifest.json`: exact build commands/exits
  and output hashes for the fixed candidate.
- `cpu-check.log`:65 actual native owner assertions through DrawList C ABI.
- `managed-check.log`:29 range seams plus existing28 scheduling/31 desktop checks.
- `actual-cocoa-cpu.log`:20 actual native/Host/Cocoa lifetime assertions with a
  synthetic Cocoa callback boundary, no window.
- `actual-appkit.log`, `appkit-results.json`:49 actual macOS programmatic
  Objective-C callback assertions through the real NSView subclass, NoAPI window.
- `prime-rml62-ime-cocoa-predecessor-build.log`: preserved actual native CPU
  regression failure on second Cocoa attachment to unchanged native focus.
- `prime-rml62-ime-clean-appkit-runtime.log`: original actual AppKit failure.
- `prime-rml62-ime-unmark-diagnostic-appkit.log`: numeric proof reused sequence1
  was correctly rejected by native Duplicate-3, leaving synthetic seed unchanged.
- `before-manifest.json`, `promoted-manifest.json`, `promoted.patch`: narrow
  production rebase onto Replay135/Studio200 and Timeline queued-action guard.
- `evidence-manifest.json`: hashes of the above raw evidence files.

The compiled predecessor/fixed binaries and original private directories remain
unchanged on the test machine; binaries are not checked into this evidence folder.
The fixed private candidate used Replay124/Studio190. Its passes qualify the
bounded input path, not the later complete production binary. Physical IME and
candidate-server/panel behavior remain unverified. See the architecture note and
standalone test README for ownership and execution limits.
