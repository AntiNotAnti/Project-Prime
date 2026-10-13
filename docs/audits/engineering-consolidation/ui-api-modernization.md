# Avalonia API modernization

Update eight obsolete calls in six shared UI files to Avalonia 12.1.2's supported
APIs: two TextBox placeholder assignments use `PlaceholderText`, and six bitmap
captures use explicit `PngBitmapEncoderOptions.Default`. The HUD UI test's two
captures use the same PNG overload. No package/version/schema change, new render
loop, input ownership, gameplay behavior or migration is introduced.

The pinned official Avalonia package XML documents `PlaceholderText` and
`PngBitmapEncoderOptions.Default` (optimal PNG compression). Every changed capture
already emits `.png` or a PNG memory stream. A device-free actual API comparison
proves the former and new encoders produce identical 92-byte PNG output for an
owned pixel fixture (SHA256 `6EACD152E5407DFD72276473D364C876A393ED62DADEA04627A767F97FA5D723`),
and old/new placeholder properties produce the same authored value. The comparison
harness deliberately uses obsolete calls for the baseline comparison; repository
source no longer does at these sites.

SDK 10.0.401/macOS arm64, fresh isolated user data and Headless.Enter before toolkit
initialization. No UiSurface/GL context, native audio device or real-account requests.
Candidate library MVID `2cdd8d54-65c7-4738-9614-0f6ee7264121`, SHA256
`40C54F1A15AD6BF86B1A0F1190811ED929CDFC0EEB2FFDB85F5D27917CEB0CA9`.

| Check | Result |
|---|---|
| Desktop Release build | PASS, 0 errors; warnings 30 -> 22, removes the eight targeted CS0618 sites |
| PNG exact-byte and placeholder alias comparison | PASS |
| Existing device-free GamepadChecks/PrimeUiChecks | PASS 1006 / 165 |
| Existing HUD UI, timeline/scrubbing/viewport | PASS 100 and related checks |
| HUD UI tool build | PASS,0 warnings / 0 errors |
| Android shared-source Release compile | PASS, 0 errors/47warnings; no targeted CS0618 |
| Physical UI/IME/accessibility and actual GL/Studio | NOT_RUN |

The Android compile is not APK packaging or a real-device acceptance result.
[Local exact log/command hashes](ui-api-evidence.json) retain the evidence.
The source-proven legacy-view removal and RmlUi readiness ledger are separate
PR #500. The post-match picker cleanup is a separate concern. No FPS/memory gain is
claimed; rollback restores the ten API call sites.
