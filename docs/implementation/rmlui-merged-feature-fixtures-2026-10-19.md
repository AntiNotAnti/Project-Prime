# Merged-feature UI fixture compatibility

Base: `63a3212219984d126a57abf54e8755e259e20738` on 2026-10-19.
This supplemental compatibility slice changes two test fixtures and this note.
It does not change production UI, match rules, protocol, movement or replay data.

## Reproduced failures

The broad PR-03 workflow reported two failures in already merged UI behavior:

- `rmlui-offline-rules-check` treated index 12 as invalid. The merged Third Person
  option occupies index 12; the current editor exposes 13 options. Running the
  unmodified fixture against the exact base reproduced the failure at index 12.
- `EditorGlAcceptance` tried to focus/click `replay_editor_close`, which
  `studio-workspace.rcss` explicitly hides in the integrated Studio. The visible
  Camera tab is the current navigation path back to playback.

The offline fixture now rejects index 13 and exercises Third Person in its
toggle and settings-copy round trips. The GL fixture verifies that the hidden
legacy control cannot focus, then clicks the visible Camera tab using the
existing native focus, geometry, pointer and intent assertions. Shared owner
retention, exactly-once retirement and stale-seek refusal assertions remain.

## Executed validation

Host: macOS 27 ARM64, Apple M4 Pro; .NET SDK 10.0.401/runtime 10.0.12.

```sh
dotnet run --project tools/rmlui-offline-rules-check -c Release
bash tools/rmlui/build-native.sh osx-arm64 gl2
dotnet build tools/rmlui-opengl-check -c Release -m:1 -p:BuildInParallel=false
# Copy the built native library and its PRIME-RMLUI.json into the fixture output.
dotnet tools/rmlui-opengl-check/bin/Release/net10.0/rmlui-opengl-check.dll \
  tools/rmlui-opengl-check/bin/Release/net10.0/rmlui --playback 960x600
git diff --check
```

- Offline editor/persistence: 91 checks passed.
- Managed GL fixture build: passed, zero warnings/errors.
- Native GL2 playback/editor acceptance: passed, 1,200 `GL PASS` checks,
  including actual Camera-tab focus/click and unchanged ownership retirement.
- Built the repository's existing pinned RmlUi 6.3/FreeType 2.13.3 bridge. The
  upstream FreeType build emits an `OS_CODE` macro redefinition warning; no new
  dependency or vendored code is added by this PR.
- Local logs: `artifacts/ui-merge-compatibility/` (not committed).

CI is not awaited at publication. The prior Linux CI failures are reproduced or
explained above; this local macOS run does not claim a new full Linux/Windows
matrix is green. Other open UI work and movement PR-04 remain independent.
