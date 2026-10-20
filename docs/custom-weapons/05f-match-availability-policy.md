# CW-05f: reserved availability contract and shared admission

A default-false `MatchDefinition.RiftlineM7Available` records frozen match
availability. It does not select a weapon, grant resources, enable a developer
capability, or activate the runtime. Personal selection remains a per-life custom
action after canonical readiness and grant admission.

The configuration reserves modifier bit18 (`262144`) and displays “Riftline M7
available” in its summary. Record copies, normalization and JSON preserve the
choice. Protocol69's accepted modifier mask remains `262143`: every session
reader, including structural UpdateMatch and archived readers, rejects the new
bit. Direct lobby admission also refuses requested availability with an explicit
protocol reason. Native-only session bytes remain identical. Public activation
requires a reviewed protocol boundary, historical rejection of the new bit, and
all host/client/lifecycle projections; that work follows this foundation.

`CustomWeaponMatchPolicy` centralizes the additional policy used by both offline
and authority runtime eligibility. It admits Battle FFA and rejects Insta-Gib,
One in the Chamber, No Imp, Balanced Mode and Enhanced Hunters. Existing supported
native modifiers and individually valid movement profiles remain available.
Unknown flags, conflicting movement profiles and negative bot counts fail closed.
Online bots are refused until custom bot integration exists. Offline native bot
targets remain supported; this does not grant them custom weapons.

Existing runtime lifecycle, replay, replica, trainer and ownership guards remain
in force. Native-only admission never invokes the custom-only policy. Neither
availability nor a successful policy check constitutes readiness, a grant, a
source certificate or resource authority.

This records the CW-05f boundary. [CW-05g](05g-frozen-public-activation.md)
subsequently activates live protocol70 availability; custom replay remains refused.
