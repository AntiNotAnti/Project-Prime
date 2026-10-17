# RML-26: native Aim Lab and training options

## Scope

The historical `pages/offline/setup.rml` already includes an Aim Lab panel, but until this slice the controls were routed back into the Avalonia offline workspace. RML-26 connects them to the current `AimTrainerDefinition` contract and the existing `AimTrainerLaunch.Create` / `StartScreen.Finish` path.

- Drills, Hunters, weapons, durations, target counts and movement policies are native, local draft choices.
- Advanced options use the authored `pages/offline/training.rml` modal for difficulty, distance, scope, headshot-only, infinite ammo, refill-on-hit, Imperialist reload style and fixed seed.
- Existing `AimTrainerDefinition.Sanitize` remains the source of truth. Imperialist Precision forces Imperialist; Timed Flick uses one target; Multi Target Flick supports four or five; Headshot Precision enforces headshots. These restrictions also disable invalid buttons.
- Native owner-thread controllers cannot start training behind an open option/arena modal. Background document intent tokens are rejected by the single shared page manager.
- A request is sent once to `Shell.RmlUiLaunchTraining`; the existing `StartScreen` verifies no active network scene/replay before using `AimTrainerLaunch.Create` and the actual game engine. The RmlUi code neither creates a training Scene nor owns targets or physics.
- The optional native Home activity 'Aim Lab' navigates directly to the native Offline page and focuses the training action. Full Settings is untouched.

## Tests and acceptance

The no-game-assets live-menu fixture checks drill restrictions, correct advanced state, options modal semantics, output seed, and single launch. Runtime tests verify the page-manager's foreground event fencing and modal retirement. Run the complete build and native software OpenGL CI.

## Remaining scope

Advanced Offline Battle **match rules**, campaign saves/ship UI, full replay library, authenticated Community management, social/party services, 3D Hunter preview, Android launcher surface/input/lifecycle and real-device acceptance remain separate independent slices.

The full Settings port remains the **last** migration phase.
