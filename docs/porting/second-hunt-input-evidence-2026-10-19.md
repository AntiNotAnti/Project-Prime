# SH-01A / SH-02: Android input ownership and direct wheel preview

Implemented: persisted Auto, Touch, AimOnly and Off modes in existing TouchSettings/Controls; canonical controller snapshot arbitration; Auto ignores resting stick noise and stays hidden after real activity until disconnect. Device/mode/dialog/layout/player-life/form changes release held touch input. AimOnly captures one pointer on either half of the screen and cannot synthesize fire, movement, jump or morph boost. Native dialogs keep their existing positional owner. Invalid settings fall back to Touch. Touch is the migration default; controller movement/buttons continue through the existing GamepadInput owner.

SH-02 is opt-in through Controls / Touch overlay / Direct Weapon Selection (controls.txt: touch_direct_weapon_selection). Its hit targets share the actual HUD sprite transform with drawing, including profile scale/offset, icon scale, pen-zone placement and aspect. One selecting pointer owns the ring; earlier aim and third fingers cannot take it. Release on the existing WEAPON completion edge uses canonical equip validation; unavailable/no-ammo/outside targets have no selection. Cancel/lifecycle releases clear capture. The default remains false until device qualification.

SH-01B finding: Project already accumulates raw gesture movement for the next simulation sample and has no upstream ten-frame hold. Existing immediate behavior, DPI sensitivity, zoom/inversion and native morph sampling are retained. The later [SH-01B follow-up](second-hunt-stylus-evidence-2026-10-19.md) adds an optional Classic timing response while retaining Direct as default; physical latency/device qualification remains incomplete.

## Local evidence

Target main after SH-00: 36a87be6b6d7a520b59697e4a64e223a5d941c54. Frozen upstream:41452f2d053fcfa679e33c00a73d120a53f59e61. Mac arm64 SDK10.0.401; Windows SSH authentication failed, so the user-authorized Mac fallback was used. Baseline Release build passed with0 warnings/errors before changes.

- dotnet run --project tools/touchcheck -c Release: new ownership/gesture/geometry checks and existing exactly-once30Hz gesture tape at30/60/120/144Hz pass.
- Release desktop build passes with0 warnings/errors; -pointercheck1790 assertions, -gamepadcheck1014 deterministic plus181 UI checks pass.
- Native Android source input54, pause38 and capabilities728 checks pass; nettest --input-edges passes sequence/lifecycle/loss/duplicate/reorder/age cases.
- Android Release builds with AOT using private JDK21.0.12.1 (0 errors;25 warnings in existing documentation/nullability/unused-field sites). No device was attached.
- HUD formula comparison across120,000 sprites, modes0/1/2, centered/top-left, four resolutions, profile/HUD/icon scales and offsets: maximum normalized-coordinate difference3.8147e-6. This is geometry math evidence, not a GPU visual acceptance claim.

The validation commands and source hashes are kept locally in artifacts/sh-integration. Exact committed-source verification is appended in the integrated status after commit; these runs describe the candidate source snapshot, not nonexistent hardware observations. Protocol61 and replay readers1–6 are unchanged; no gameplay cadence, network authority, shot aim or inventory owner was added.

Unproven/disabled: physical Android touch latency, controller disconnect/OS lifecycle feel, actual highlighted weapon equip, high-DPI visuals and combat/replay device acceptance. Direct wheel selection remains off and Auto/AimOnly require explicit choice. No real game-source media or GPU playability is claimed. Rollback: choose Touch and disable Direct Weapon Selection, or revert this slice. Merge is authorized without waiting for review/CI; CI is not represented as green.
