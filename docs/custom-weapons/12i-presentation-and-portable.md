# CW-12i — portable exact packages and realtime replay audio

Custom portable bundles use manifest version2 with the fixed member
`weapon.ppweapon`. Export acquires the exact historical archive lease and checks
the complete recorded package row. Import enforces the 8 MiB package limit,
strict package parsing and exact archive/content/dependency/mechanics identity,
as well as replay record validation. Native bundle version1 remains supported.
Import never installs gameplay content. If the exact package is not installed,
the validated package remains in the private import directory with an actionable
installation message. A dedicated accept/install interaction remains follow-up UI
work; manual explicit installation is currently required before retrying import.

Realtime custom playback and killcams acquire the existing presentation audio
owner and use the retained package PCM. Per-life cursors play accepted launch,
reload and bolt cues, suppress seek/catch-up/baseline cues and stop on pause,
replacement, disposal or loss of ownership. Native replica sound guards remain
intact. This does not implement offline custom PCM mixing for exported video.
Killcam weapon names join canonical lethal custom contacts to the complete
killer/victim life and authority frame before using the exact definition name.

Desktop Release compilation succeeded: zero errors, two existing warnings,
MSBuild79.08s. Binary SHA-256:
`af0d4aefc65b8061a0b5f9c588812bbc57d8c91541809d4ca2f320172c6deb15`.
The build also included uncommitted Community validator work; it is compilation
evidence only. Receipt: `artifacts/custom-weapons/CW-12i/compile/desktop.json`.
All remaining tests are **NOT RUN** per the user's instruction; Android remains
deferred. Bot integration, offline export sound and owner lens transitions remain
CW-12 implementation gaps, and actual server recording qualification is unrun.
