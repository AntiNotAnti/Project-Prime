# Custom loadout Rules UI capture

This tool captures the actual Avalonia offline Rules and lobby Rules controls with M7 availability off/on at 940×528 and 360×800. It also invokes the actual offline Start callback after choosing Survival with a saved M7 opt-in and checks refusal before launch/preference writes.

Run only after the coordinated production build is complete. Build the tool with `BuildProjectReferences=false` so the accepted product DLL is retained. Set `PROJECT_PRIME_USER_DATA` to an isolated directory and pass an absolute output directory when running the tool. It writes nine PNGs, the production `UiCapture` bounds JSON beside each PNG, and `loadout-ui-evidence.json` with product hash/MVID and target geometry.

The session and settings are synthetic fixtures. Actual production controls, modal-opening/start callbacks and Skia layout rendering are exercised. This does not qualify physical input, UDP, native RmlUi rendering or Android devices. Inspect the PNGs and bounds for toggle/help clipping; geometry is evidence, not a substitute for visual inspection.

Pass `--rules-save` and an absolute output directory to run the separate actual `StartScreen.RmlUiSaveOfflineRules` acceptance instead of captures. This writes only the isolated `Savedata/settings.json`, exercises stale/invalid/canceled refusals and filesystem-failure rollback, then tests the real settings-replacement fence. Startup completion is explicitly staged; the production save owner and serializer are unchanged. It emits `rules-save-ui-evidence.json` and restores the isolated process's restart-fence flag.
