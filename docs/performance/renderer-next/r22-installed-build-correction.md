# R22 installed-build follow-up — 2026-10-22

The report of a slight camera-following bright region remains visually open.
Passing shader fixtures does not close a motion/appearance report.

The operator's `%USERPROFILE%\Desktop\New folder (2)\ProjectPrime.exe` contained
the initial R22 assembly, without `scene_native_fog_blend`. Its bundled game
assembly SHA256 was `7b2c0dc24b48bfe7716fa861569226a4703a0e8c3cffb83647e1846ce680ba08`.
Earlier correction tests had exercised the renderer worktree, not that package.

The fog compatibility and frozen-camera upload corrections and their focused
tests were carried surgically into the primary checkout. A Windows game/Studio
package was rebuilt with the existing primary HUD/map changes and verified native
UI runtime. A first attempt hit another compiler's file lock; the successful
build used SDK artifact outputs outside the shared checkout.

Installed version: `0.35.1-local.hud-r22fog.20261022`.
Game executable SHA256:
`fa29dc50e7d3338e7b08352aa0f9cf74aa6dfa4f15648830f98449f9c497eccf`.
Both packaged heads contain the corrected game assembly SHA256
`3ca194c288f8d91078a82984bfaa04bbb6b526cc36fb208edb289ae7d4e99fda`.
The actual packaged executable passes 579 GPU assertions/42 captures, and all
17 native reference captures remain byte-identical.

The 375 existing manifest-owned runtime files matched their old hashes and were
backed up before replacement. The new ownership list is identical; there are no
stale removed files or new user-file collisions. Installed hashes were verified
before the ownership manifest was replaced. Unowned game data, settings, saves
and credentials were not copied or replaced. Backup and receipts remain under
`%LOCALAPPDATA%\ProjectPrime\r22-installed-build-20261022`.

Two controlled idle camera orientations with fog toggled and shadows disabled
show small On/Off background differences; these are not a continuous-motion or
fully translucency-excluded test. Native combat particles still blend in linear
light and can appear brighter. The shadow volume is centred on camera position
plus facing times 24, so its finite coverage is another candidate for a moving
boundary. Neither has been established as the operator's remaining symptom.
After restarting the corrected executable and entering a new scene, reproduce
with shadows Off first, then isolate native translucent effects if it persists.
