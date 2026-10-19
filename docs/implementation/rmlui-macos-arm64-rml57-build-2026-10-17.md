# RmlUi macOS Apple Silicon snapshot — October 17, 2026

The user requested a runnable arm64 build while parallel implementation continues.
The delivered archive is `/Users/jarrett/Downloads/ProjectPrime-RmlUi-macOS-arm64-rml57-20261017.zip`
(82,342,080 bytes), SHA-256
`52c073df79c9bde65dd4260115a98a7d8bfd0cd8c5afe7e8adc2662468ceda18`.
Extract it and open **Project Prime RmlUi.app**. The bundle launcher enables
RmlUi and uses its bundled arm64 .NET 10.0.12 runtime. The application is
ad-hoc signed, without Apple notarization.

The package uses the compiled RML57 checkpoint, client SHA-256
`1ddbd9daf2e1c1957b7c2abd1ce669851d013a70c383602c7b2a51cb6b41b265`
and GL2 native SHA-256
`e325a8242888b0d493f67a86c40bbb8e53821a3e3f48377a936e98c5728ee3ef`.
Its managed action families remain Studio55, Replay48 and InGame34. The shared
checkout is dirty; this is not a clean-head build or a compilation of the newer
Studio85, Replay68, Android cycling or Party group-reservation work.

All 106 RML/RCSS files match the copied checkpoint except two separately
qualified InGame resources: `pause.rml` and `match.rcss`. Their status semantics
and caption/Resume separation passed 258 baseline plus 76 capability/layout
checks at each of 640×360, 1280×720 and 1920×1080. The composed qualification is
`artifacts/rmlui-checkpoint-rml57-ingame-fixed-20261017/combined-qualification.json`.
Prior failing observations remain in the original and diagnostic checkpoints.

Package validation passed bundled startup, GLFW extraction paths, KTX loading
and required exports, all26 Mach-O arm64/signature checks, deep strict bundle
signature verification, authored-asset comparison, shipped-map dependencies
and the existing no-game-assets guard. The actual packaged client rendered and
resized its launcher at 2560×1536 and 2200×1480 on Apple M4 Pro/OpenGL2.1.
After extracting the archive into an unrelated temporary path, all429 file
hashes/symlinks and executable permissions matched; signature verification and
bundled startup passed again. Test launches used private user-data directories.

`BUILD-INFO.json` inside the archive contains exact file hashes. Packaging
inputs, launch-wrapper source, logs and extraction acceptance are preserved in
`artifacts/ProjectPrime-RmlUi-macOS-arm64-rml57-20261017/`. Managed and native
client resources live under `Contents/Resources/client` so the bundle seal does
not treat loose managed assemblies or UI assets as nested executable bundles.

No cartridge data, extracted assets, account credentials or user saves are
included. Normal user launches retain the existing Project Prime user-data
location. Full Settings/advanced editor parity, live backend qualification and
release/device performance acceptance remain open. CPP-01 remains complete by
the user's skip decision, without an AGX or device pass.
