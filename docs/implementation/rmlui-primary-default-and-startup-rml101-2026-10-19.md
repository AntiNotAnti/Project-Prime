# Primary desktop RmlUi and Windows startup artwork — 2026-10-19

RmlUi is now enabled by default in ordinary desktop builds. The existing compatibility property `MphReadRmlUiGlSmoke` defaults to true when unset for clients; an explicit false remains available for developer diagnostics, and dedicated-server builds omit it. Existing fail-closed native startup behavior is retained. The game product and managed apphost remain Project Prime / ProjectPrime. Studio remains a separate Project Prime Studio application.

## Build and package behavior

Ordinary build/run/publish copies the matching locally built native library, manifest and license files alongside authored resources. Without a RuntimeIdentifier, the SDK host RID selects the native artifact. Source-only/headless compilation does not require CMake or a native library; an interactive client requires the pinned bridge, built with `tools/rmlui/build-native.sh auto gl2`. README documents this path and the complete Windows pair packager.

Standard Linux and macOS CI/release packages now build and verify the native bridge before shipping. Windows CI uploads the complete verified game/Studio pair from its native Windows job, replacing the incomplete Ubuntu cross-published client artifact. Windows release already uses the pair packager. macOS verification runs again after signing; local ad-hoc re-signing retained the exact native library hash. Server rows are unchanged. Workflow syntax was parsed; hosted Actions execution is separate from local qualification.

## Missing Windows Press Start artwork

The old Windows package contained the correct TGA, but its startup screenshot showed only the title and prompt over a flat dark field. RmlUi stores a document source URL from the path supplied to LoadDocument. Its StreamFile converts drive colons to URL-safe pipes, but backslash-only paths reach URL parsing before image base-directory resolution. ElementImage then obtains an empty directory from URL.GetPath. Stylesheet joining separately normalizes backslashes, explaining why the layout and fonts still worked.

The bridge now passes `filesystem::path::generic_string()` to both document loading and debug reloading. Forward slashes preserve the Windows document directory for relative images. The managed terminal GL pass also saves, sets and restores the client texture unit, in addition to the server texture unit. This prevents inherited engine client-array state from corrupting fixed-function image coordinates.

Desktop package verification now requires the startup document, stylesheet and artwork. An actual missing-artwork negative fixture was rejected and restored successfully; Android foundation payload requirements were not changed.

## Verification

- Ordinary desktop build without an opt-in flag: success, zero warnings/errors; matching native files and authored assets staged. Evaluated defaults confirm RmlUi=true, server UI omitted, explicit false respected, and AssemblyName=ProjectPrime.
- Fresh native GL2 build from the pinned dependencies and current bridge; full native package validation passed.
- macOS actual startup GL acceptance: **37 PASS**, including detailed artwork pixels, contaminated nonzero client texture-unit rendering/restoration, aspect ratio, pointer, Enter, Space, and one-shot foreground routing.
- Windows actual startup GL acceptance: **37 PASS**, exit zero. The identical new checker using the previous Windows native DLL fails its artwork/layout assertion (negative control). This is an image regression test, not just a window-liveness check.
- Final Windows package: true packager exit zero; focused GL test build zero warnings/errors; final game/Studio matrix **16 PASS**, including separate roles, exact loaded native module, repeat rendering/resize/exit, forwarding and Studio survival after game exit.
- Actual final Windows game startup captured and visually inspected: artwork visible. The legacy `-shellshot -shelllifecycleonly` driver still exits one because its legacy startup-control lookup cannot drive the native gate. Its startup capture is useful evidence, but that driver is not reported as passing. Native pointer/keyboard gate routing passed independently above.

Receipts and before/after captures: `artifacts/rmlui-default-20261019/` locally, and `C:\Users\Jarrett\Documents\ProjectPrime-Windows-QA-20261019-102314\startup-background` on Windows. Early failed compiler checks and null-exit instrumentation attempts are retained; final accepted package is **candidate-build4**, not candidate-build3. Windows sources are the frozen previously qualified pair plus the explicit default/frame/document-path/test changes; concurrent networking edits in the main workspace are excluded.

## Downloads and identity

Corrected Windows ZIP: `ProjectPrime-Windows-x64-rml101-20261019.zip`, **180,441,008 bytes**, SHA-256 `b6a974d1884723695b405ab42931735f886d191c46a61ffc791c47273cfd2014`. Both executable versions remain 0.35.1, a local candidate, not a published release. Keep ProjectPrime.exe and ProjectPrimeStudio.exe together with shared resources.

The previous Mac RML99 delivery is preserved. A separate identity-only copy is named `ProjectPrime-macOS-arm64-rml100-20261019`: game bundle `Project Prime.app`, bootstrap `ProjectPrime`, name/display name `Project Prime`, and identifier `com.projectprime.game`. Both app signatures verify and the renamed game compatibility smoke passes. This copy preserves the previous game/Studio code; it is not a fresh build of the Windows-specific image fix.
