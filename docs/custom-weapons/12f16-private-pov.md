# CW-12f16 — private first-person archive inspection

This source contract covers explicit custom archive inspection only. Local
qualification and limits are recorded in the [evidence ledger](evidence/CW-12f16.md).

The private camera FOV projects an accepted custom optic through the exact
historical package's settled lens. It is applied before first-person viewmodel
preparation without writing `CameraInfo` or simulation state. This is settled
scope projection only: recorded owner zoom transitions are unavailable, so no
historical owner transition fidelity is claimed. An independent magnification
oracle is exercised by v3 qualification.

`SceneCustomInspectionCamera.cs` adds a private picture subject selected through
`SetCustomInspectionFirstPerson(slot)` and released through
`ClearCustomInspectionFirstPerson()`. Selection requires an active custom archive
inspection session, a recorded pose cursor, an admitted player with nonzero
generation/life and health, and a matching loaded active/spawned biped. Dead,
spectating, alternate-form, morphing/unmorphing and non-first-camera subjects
are refused. This does not grant a gameplay weapon or create an input owner.

The selection records generation and life. Each picture rechecks those identities
and eligibility; an invalidated subject clears to a stationary roam camera at
the last picture eye. No live controls or global spectator state participate.
Selecting a camera does not permanently change canonical `MainPlayerIndex`.
The renderer borrows the selected subject inside a disposable picture scope,
then restores the original main-player ownership. Both draw and render paths
enter that scope. The selected path updates its camera without invoking ordinary
replay camera control.

Private camera selection is outside the canonical world schema. It introduces no
packet, archive, descriptor, checkpoint or world layout version. Current protocol85
and historical formats remain retained. Tests must compare strict canonical
graphs across repeated pictures, backward fresh restores and actual suffix
continuation, rather than masking selected-player or draw mutations.

The inspection uses accepted recorded native/custom state and existing private
pose/fire projection. Historical owner presentation sidecars are absent: a
reconstructed first-person picture does not establish captured owner-camera,
input, animation timing or audio fidelity. Explicit inspection remains silent.

The renderer suppresses the foreground retail HUD specifically for explicit
custom inspection. Private recorded actors do not initialize the retail HUD;
initializing its canonical models/state merely to draw would mutate the restored
world. The separate `ReplayPresentationHud` callback remains available. This
slice covers camera, native/custom models and FOV, not retail visor/ammo overlays
or captured owner-HUD fidelity.

Ordinary custom recording, theatre, export and killcam admission remain closed.
This slice does not qualify every hunter/form, audio, physical devices or bots.
Android testing remains deferred; no CI or performance claim follows from this
source contract.
