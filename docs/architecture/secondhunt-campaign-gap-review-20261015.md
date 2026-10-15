# Campaign gap review: SecondHunt and Fruity Prime (2026-10-15)

**Compared:** Project Prime main after CAMP-01..07, SecondHunt campaign/frontend reference and Fruity Prime PRs #107 and #150. Use the shipped Project Prime native `Scene`, `StorySave`, 60 Hz simulation, Avalonia launcher, sound device, ROM extraction and historical protocol/replay readers as the baseline. This is a comparison of capabilities, **not** evidence that a cartridge-backed end-to-end campaign has been played.

## Priority 0: unblock single-player progression

**Fixed in stacked PR #571:** the first Celestial Archives scan tutorial is an original DS `DialogType.Okay` modal, not an instruction to hold a nonexistent physical touch pad on a Mac. Previously the modal's `PlayerDialog.CheckButtonPressed` accepted only pointer clicks in DS-space rectangles, even when a keyboard/controller owned input. Enter/Space/Jump-confirm and Escape/Morph-decline, left/right page navigation and an on-screen desktop hint now drive existing dialog choices. The original pointer route remains. On default controls after dismissal, `E` enters the Scan Visor and `Q` performs a scan. Do not silently complete the tutorial or skip its source-script progression.

**Fixed in stacked PR #572:** the Scan Log table has sparse numeric `L...` identifiers. The bit position in `StorySave.Logbook` is the **cartridge record number**, not the entry's ordinal position. `CampaignJournal` now accepts only bounded `L000..L543` IDs, reads their saved bits and never discloses unscanned text. Frozen save bytes, scan authority and archive format are not rewritten. Sparse-ID regression covers missing 30/31 and malformed entries.

## Priority 1: Sylux's visible Delano 7 ship

**Implemented in stacked PR #572** based on Fruity [#107](https://github.com/liveteklol/Fruity-Prime/pull/107). When the original native `SamusShip` platform is in single-player with Sylux, retain its invisible rig, collision and script messages, and draw the locally extracted `SyluxShip` with `SyluxTurret`. Use original animation metadata for Delano takeoff and hide at the authored sleep state; avoid Samus-specific nozzle effects. Exclude multiplayer and detached replica worlds. Headless eligibility tests do not certify live ship animations, platform fit or player/collision geometry: test landing/takeoff/re-entry in five planets with a real cartridge on desktop/Android.

## Priority 1: original film lifetime and cancellation

Fruity [#150](https://github.com/liveteklol/Fruity-Prime/pull/150) fixes a native C++ scene-lifetime use-after-free involving movie tasks. Project Prime's C# analogue previously launched long-lived audio/image tasks that could read OpenAL sources and scene frames after `StopMovie` deleted their resources. This was a **source-confirmed race risk**, not a reproduced C# native-device crash.

**Candidate follow-up:** `CampaignMovieDecodeSession` now owns only per-playback VX decoding, with privately allocated frame/sample buffers, its own cancellation token, and completion-observed task retirement. No decoder callback captures Scene, GL or AL; old sessions may finish retiring after a new movie starts without sharing mutable decoder state. The 60 Hz owning scene step queues OpenAL and commits both 15 Hz image frames together, and owns their fade completion. Stop and `Scene.DoCleanup` cancel the session before deleting audio; teardown cannot chain a movie, resume a room or quit the game. Headless/replica paths skip presentation and follow the existing after-film action. This retains the authored two-part bad ending, pause-counter handoff, skip/fault fallback and native `StorySave` lifecycle.

`--campaign-movie-lifetime` tests cancellation without a blocking wait, two worker retirement, task failure, repeat stop and decoder/buffer isolation. **Full release acceptance remains pending** until actual Mac/Windows/Linux/Android cartridge-backed landing/takeoff, bad/good Gorea, skip/re-entry, close-during-audio, no-device, audio sync, 15 Hz image pacing, and shadow/GL lifetime are compared on a physical build. The original Fruity C++ patch is not pasted into the C# runtime.

## Missing SecondHunt campaign presentation (ordered)

| Feature | SecondHunt evidence | Current Project Prime status | Recommendation |
| --- | --- | --- | --- |
| Interactive first-person cockpit and deep-space planet selection | `Frontend/ShipMenu.cs`, `ShipPlanetSelect.cs`, `DeepSpaceView.cs`, `ShipBriefing.cs`, `docs/VANILLA_SHIP_AND_PAUSE.md` | Functional two-page Avalonia ship UI, original soundtrack IDs, verified story travel; not the full 3D ship | **P1**: optional original-art cockpit/briefing layer from locally extracted files, retaining existing travel/save handoff |
| Multi-page real cartridge ending credits, completion records and save-card messaging | `Frontend/GameEnding.cs`, `docs/ENDINGS.md` | Both ending FMVs and Project Prime results/attribution view, `CREDITS` sequence, no full seven-page DS animation | **P1**: proper good/bad credits/results flow, immutable save timing and attribution as distinct views; require source ROM/device parity |
| Exact 16:9 single-screen pause/map with detailed connector view | `Frontend/PauseMap.cs`, `AreaMapModel.cs`, `docs/VANILLA_SHIP_AND_PAUSE.md` | Existing 3D native map, viewport projection corrected in CAMP-05, visited-room scan journal; no full ROM-textured integrated mode | **P2**: native projection/panning, visible-only connectors and filters; no second game Scene or renderer |
| Slot metadata, character choice, played time, known planet reveal and saved ship marker | `Campaign/CampaignSaves.cs`, `ShipMenu.cs` | Project Prime retains original three `save###.json` slots, checkpoint/areas but fewer separate campaign metadata fields | **P2**: backward-compatible optional sidecar, never convert or overwrite existing saves; apply only after opt-in data migration |
| Fast campaign ending/boss test automation | `Campaign/BossSkip.cs`, `GoreaSkip.cs`, `docs/ENDINGS.md` | Device-free mocked exits/ending catalog and optional native launch smoke | **P2 developer-only**: use actual boss hit/state scripts for accepting both endings, gated from shipping/online |
| Controller weapon ring and touch/stick controls | `Campaign/WeaponRingInput.cs`, app `CampaignGyroAim.cs` | Project Prime uses shared existing weapon-wheel, native input and configurable gamepad/touch | **P2**: compare actual controller/touch behavior before adopting another ring or introducing duplicate input authority |
| More source music/ambient quality modes | `Campaign/CampaignAudio.cs` | Existing MusicPlayer / Sfx and CAMP-06 scoped ship/credits audio ownership | **P2**: benchmark crossfade, preserve single mixer and explicit device-loss/Android semantics |

## Other upstream differences to assess separately

- Fruity #106's *Dialanche* combat parity, #111 aiming and #150 movie teardown are useful **potential correctness** changes, but they are C++ native implementations. They need native C# state/prediction mapping and real room tests before adoption.
- Fruity #156's 30 Hz animation interpolation may improve high-refresh visual quality; verify it does not conflict with Project Prime's 60 Hz simulation and existing high-refresh pose late-latch paths. This is **not a campaign progression fix**.
- Fruity #140's Instagib rail is a multiplayer feature, not a campaign requirement. Preserve Project Prime's own distinct Instagib visual design.
- No copyrighted models, audio or texture files are copied from either upstream repository. Original game resources must come from the player's own legally extracted cartridge.

## Acceptance before enabling default native presentation

Use CAMP-07's `docs/architecture/camp07-acceptance.md` and the existing `--campaign-native-scene` probe, then verify the Mac tutorial by pressing Enter -> E -> Q, scan dialog paging, controller A/B, ship Yes/No, Sylux landing and takeoff without stale collision/nozzles, default Samus, Android touch, Gorea ending chains, and normal multiplayer/replay visibility. CI verifies policies and compiled code, **not** physical playability; log native/device evidence by exact commit.
