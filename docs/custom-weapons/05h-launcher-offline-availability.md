# CW-05h — launcher choice and offline human availability

`MenuSettings.RiftlineM7Available` defaults to false, including older settings.
The offline Rules sheet and owner-only lobby rule editor expose the choice.
Existing movement/camera indices remain stable; M7 appends at offline14/lobby18.
The native inventory, wheel and weapon unlocks retain their existing meaning.

Offline Preview freezes availability into `LaunchPlan.MatchRules`. Admission
requires Battle FFA, compatible modifiers and no active recording; native bot
opponents are allowed. Create persists preferences only after validation.
Direct launch repeats admission, including routes that return early for
Adventure/demo. Rematch retains the frozen choice. Online/Host still obtain
effective availability from the frozen server definition, never this preference.

After the real offline scene loads, MatchStart attaches a scene-owned runtime
that captures the exact local human in slot0. Fixed simulation steps wait for
spawn before granting4/16, initially unselected. Bots and other actors cannot
receive this public inventory. Death retires the life and its flights; native
respawn creates a fresh unselected custom life. Existing custom input selects it
without changing the current native weapon or its ammunition.

The runtime rechecks actual mode/rules/ownership. If activation fails after load,
the launch reports its reason, ends the scene and returns false before readiness.
Scene unload disposes inventory/projectiles/registry. M7 scenes refuse native
recording, killcam and replay until CW-12 provides a compatible custom format.

Rollback is to leave availability off; ordinary plans attach no custom runtime.
The separate developer fixtures remain explicit and retain their test grant API.
This slice does not complete CW-05: experimental Android RmlUi settings still
need the UI-owner asynchronous save adapter (05i), and accessible in-game
selection plus final platform acceptance follow. See [evidence](evidence/CW-05h.md).
