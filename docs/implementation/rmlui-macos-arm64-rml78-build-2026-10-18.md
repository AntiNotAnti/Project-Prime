# RmlUi macOS Apple Silicon snapshot — October 18, 2026

The requested copy is [ProjectPrime-RmlUi-macOS-arm64-rml78-20261018.zip](/Users/jarrett/Downloads/ProjectPrime-RmlUi-macOS-arm64-rml78-20261018.zip).
It is 85,153,032 bytes; SHA-256
`874b2dbdbbdc9c9f14e8390d99b1ee46018a0eb61f57596f9806b22d03b07664`.
Extract it and open **Project Prime RmlUi.app**. The launcher enables RmlUi
and uses its bundled ARM64 .NET 10.0.12 runtime. Signing is ad-hoc; the app
is not Apple notarized.

This is a fresh build of the integrated RML-78 source checkpoint on base
`dc7f097067d755ec76d145fc7cc333068566863d`, with zero warnings/errors.
All 1,811 source inputs match that checkpoint before and after packaging.
The managed client is byte-identical to the previously compiled RML-78 client:
`3745c8f3d51aa1c733670651ab0eff330b35da13a926ca0d4a9701af4e91343d`.
The native RmlUi input is
`9174d430969ac3ed937e2e491e756e39cd3c00d935b88f287212acbf4a700f97`;
the packaged hash after signing is recorded in `BUILD-INFO.json`.
The dirty working tree is disclosed; this is not a clean-head release.

All 26 Mach-O components support ARM64 and pass signature checks. Deep strict
bundle verification, all 124 authored RML/RCSS comparisons, shipped-map
dependencies and the no-game-assets guard pass. An independent ZIP extraction
preserves all file hashes, links and mode bits, and passes bundled startup.
After the user unlocked the Mac, the extracted app passed GLFW-path checks and
rendered/resized the native launcher at 2560×1536 and 2200×1480 on Apple M4 Pro,
OpenGL 2.1 Metal 91.7. The earlier no-active-display refusal is preserved.
This qualifies packaged launcher startup and rendering, not full gameplay or
release performance.

The main game Settings controls are unchanged from the RML-57 copy. Quick
Settings still contains Reduce Motion, Automatic Updates and Debug Logging;
All Settings uses the existing settings owner. Additional native Studio controls
include snapping, cursor, local-transform and pivot options. Theatre export
includes preset, resolution, FPS, encoder and HUD/overlay options. The separate
Full Settings rewrite remains private and unintegrated.

This package includes the integrated RmlUi, networking/presentation fixes and
local Echoes catalog work. Private Trooper drone, Echoes movement, Trilogy
reader, all-material GX and League startup candidates remain excluded pending
integration. Live backend/account and final release qualification remain open.
Full campaign and ending validation are excluded under the user's revised plan.

No cartridge/disc data, extracted game assets, credentials or user saves are
included. Normal launches use the existing Project Prime user-data location;
qualification launches used fresh private roots. Packaging logs, source guards,
initial staging failures and extraction/render receipts are preserved under
`artifacts/ProjectPrime-RmlUi-macOS-arm64-rml78-20261018/`.
