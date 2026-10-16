# Project Prime RmlUi reintegration: RML-01 foundation

**Status: restored host foundation; no launcher cutover.** This is a
source-and-contracts PR, not a claim of rendered, shipping RmlUi parity.

- Current-main baseline: `853c16a66965c496ea957382d65247bc6f15e4e0`
- Historical donor: `backup/main-2026-10-09`
  (`d08107a8a7a7ca64f87e96780aff8c6646cc2443`)

## This slice

- Restore the managed `RmlUi/Host` source: owner-thread native ownership,
  versioned actions, generation-scoped document lifetimes, immutable snapshots,
  input mapping, cancellation and teardown.
- Restore the C++ `native/rmlui-poc` bridge source. Its GL2 path targets our
  existing OpenGL context; the optional draw-list ABI is source only at this
  stage. **No WebGPU, Metal, Vulkan or DirectX renderer is restored.**
- Restore `RmlUiPointerMapping` and the independently runnable fake-native
  `tools/rmlui-runtime-check` suite.
- Run the contract suite in PR CI and exclude the host from dedicated-server
  compilation.

Current `GuiLauncher`, `Shell`, `UiOverlay`, current `PrimeRouter`,
match/lobby/Community authority, campaign and Studio stay unchanged.
**Avalonia is still the shipping/default launcher.** This PR does not invoke
the native bridge during the game loop or package it in release outputs.

## Acceptance and local checks

```sh
dotnet run --project tools/rmlui-runtime-check -c Release
dotnet build src/MphRead/MphRead.csproj -c Release
dotnet build src/MphRead/MphRead.csproj -c Release -p:MphReadServer=true
tools/check-opengl-only.sh
```

The standalone test suite uses a deterministic fake bridge by default, checking
the protocol, stale document/intent rejection, focus, input, lifecycle and
error recovery. It does **not** prove native ABI binary compatibility, OpenGL
rendering, font/asset packaging or real-device input. `--native` requires a
compiled matching bridge and a matching RmlUi asset directory.

For an optional native build, see `native/rmlui-poc/CMakeLists.txt`.
Supply RmlUi 6.3 source as `RMLUI_SOURCE_DIR`, C++17 and FreeType.
The historical pinned dependency versions were RmlUi
`ba95ffe8bfb6370efb2cdcca927eaad4710c5413` and FreeType
`42608f77f20749dd6ddc9e0536788eaad70ea4b5`.
Do not publish an unverified standalone build as a client release.

## Next independently reviewable slices

1. **RML-02 OpenGL compositor:** add the engine-owned UI frame hook after
   scene/HUD and before present. Use the current GL context and restore
   stencil/scissor, blending, depth and frame state, including on resize.
   Do **not** port obsolete `ModernGraphicsCompat`.
2. **RML-03 opt-in native launcher:** separate current UI services from
   Avalonia views; preserve match/session ownership, settings and game
   return-to-lobby flows. Build a runtime selector with explicit recovery.
3. **RML-04 Home/Play/Lobby pages:** rebase the historical layout/assets,
   Deployment Chamber and Hunter previews on current services. Restore
   fonts, licensing and native library packaging; confirm real input.
4. **RML-05+ parity:** current multiplayer rules, Hunter License,
   Community, Social, Adventure, overlays and Studio deep links; Android
   keyboard/touch/surface lifecycle and per-platform acceptance.

No automatic native crash fallback is implemented by this foundation PR.
