# CW-05e — scope, canonical HUD and reversible feedback

Custom optics own separate presentation state. They never set native
`EquipInfo.Zoomed`, select a native weapon, or borrow its zoom/assist profile.
The definition supplies magnification; full lens FOV is
`2 * atan(tan(normalFov / 2) / magnification)`. The existing user-FOV projection
adapter applies once. Both simulation and late-render aim use the same tangent
ratio and per-optic sensitivity. Native scope math remains unchanged.

Public settings choose toggle or hold and independent2.5x/4x sensitivity for all
aim devices. Values persist through the normal validated preferences path;
sensitivity defaults1 and clamps finite values to0.1–3. Hold reconciliation is
bounded by physical, selection and mode transitions. A blocked release survives
until publication is allowed; preheld Zoom can scope a new Equip. Denied or
queue-refused held requests never retry continuously. Hold-to-toggle preserves
current optic without inventing a press.

The fixed simulation owner captures a read-only HUD value after input and after
native reconciliation. Rendering cannot advance offline resources, reload time,
source sequence or a recipient queue. Magazine/reserve and reload phase come
from canonical state. Local selection/optic prediction is cosmetic and reversible;
resource numbers are never predicted. Readiness or owner/bootstrap/manifest loss
removes stale scope and uses unknown resource feedback while preserving the
independent native emission fence. Public recipient state supplies watched-player
resources; local predictions are never borrowed by another POV. Custom replay
remains deliberately gated until CW-12.

Classic, Project Prime and Custom HUDs receive an M7 magazine/reserve/status
readout. Native fallback icons and selected-row highlighting are excluded while
custom selection is visible. The M7 inventory row is separate from native wheel
sectors and icon arrays. Custom crosshairs use the generic profile rather than
inheriting the fallback or Imperialist override; the built-in reticle is simple
asset-free geometry.

Fire produces a bounded six-fixed-step reticle pulse, suppressed by reduced
motion. Receipt denial cancels its matching prediction. Empty is distinct from
cooldown/capacity/reload refusal and produces visible status plus a generic audio
fallback once for the current physical action. Accepted results do not replay the
local pulse. Pending feedback never mutates camera source aim, ammunition or HP.
A64-result, full-stream/current-owner queue and64-sequence dedup window retain
multiple results between frames. Overflow cancels cosmetics, retains gameplay
state and does not invalidate readiness. Late older results cannot replace
feedback for a newer physical action. Scene/life/readiness replacement clears
cosmetic ownership.

Public launch/contact facts are drained once per fixed step and retained in a
bounded recent64-fact presentation buffer through60 steps. This prevents the
1024-fact recipient queue from stalling an active client merely because no HUD
read occurs. These facts cannot enter native combat. Original model, audio,
tracer/impact presentation and rig attachment remain CW-06 work.

This slice does not complete the opt-in loadout selection flow or final device
acceptance. Developer activation remains opt-in; physical controller/Android,
WAN and final release qualification remain explicit gates.
