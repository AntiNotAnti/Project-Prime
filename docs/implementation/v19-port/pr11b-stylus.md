# V19 PR-11B: contact-driven stylus follow

**Feature 46**, and part of **47**. Integrates with the existing pre-RmlUi pointer/stylus system rather than swapping the input stack.

Physical pen contact in the active aim region is now a distinct signal (`PointerDevice.AimContact`), separate from fire button state. Hover never requests follow, and any contact owned by the UI, weapon buttons, native wheel, or zone placement is excluded by the existing input quarantine.

When the main player is in first-person Dynamic/Metroid presentation with stylus active, contact follows the actual shot vector with the native 0.1-per-simulation-step easing coefficient. Without contact, dynamic camera follow pauses and aim direction remains steady. The shot direction, network input and 60 Hz simulation cadence are unchanged. The static/Quake response path is unaffected, and non-stylus mouse/controller Dynamic keeps its prior easing.

PointerCheck checks contact, drag, release, WPN ownership and UI quarantine. Native pen/tablet, physical driver handoff, crosshair/camera visual agreement and Android acceptance are still required. The coefficient is not a claim of exact MelonDS timing.

This branch is not the 4:3/5:3 stacked-screen work, which remains PR-02. It does not change any packets, stored replays, cartridge data or standalone Studio process.
