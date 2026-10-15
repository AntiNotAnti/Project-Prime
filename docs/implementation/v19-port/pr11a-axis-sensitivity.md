# V19 PR-11A: axis sensitivity with decimal editor

**Feature 45 completed in this slice.** Two independently saved axis multipliers affect local mouse biped and alt-form look, and the remote-network late-aim presentation path. They compose with normal mouse sensitivity, Imperialist zoom sensitivity, and the existing invert-axis controls.

The Avalonia Controls > Mouse page gains two compact `DecimalSliderRow` fields (0.01x–3.00x), including keyboard/numeric input. The same value is captured by `SettingsDraft` for Apply/Cancel/dirty status. Reset-to-defaults restores 1.0x for both. The reusable row is a prerequisite for PR-10's wider decimal migration.

`InputSettings` writes `horizontal_sensitivity` and `vertical_sensitivity` into `controls.txt` using invariant decimals. Schema/archives validate both as finite numbers. Legacy controls files default to 1.0x. No packet sizes, simulation cadence or damage logic change.

A new asset-free `--axis-sensitivity` nettest checks local/scoped arithmetic, finite bounds, and actual file persistence, and is invoked from the existing Linux nettest CI group.

Remaining PR-11B work: stylus contact-driven follow/camera/reticle agreement (features 46–47), plus native digitizer/Android acceptance. No CI wait; latest full workflow to be evaluated after the final PR for this batch.
