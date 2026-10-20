# CW-05r — public online lives start without the rifle

The public Riftline M7 availability flag now controls acquisition eligibility,
not a spawn loadout. The actual dedicated-server configuration freezes a
pickup-only mode; the existing developer fixtures retain their explicit rifle
loadout. Public mode wins when both switches are set. No menu or input packet can
fund a public life.

An empty life still binds its clock, native selection history and immutable
accepted source observations through the normal post-readiness engine boundary.
This is necessary for native firing to continue before acquisition. The private
state reader reports that bound life even when there is no custom inventory; it
does not create ownership, advance resources or bind a life from a state request.

Private custom state packet90 is now114 bytes: its existing113 bytes followed by
a strict boolean `HasWeapon`. False requires no selected custom weapon and an
exact normalized absent snapshot: full owner/definition identity, committed
source tick, invalid phase and zero ammo, optic, timers and shot IDs. Ownership
is explicit; an exhausted owned rifle remains owned. Publication revisions and
quarter-second retries recover loss; the ownership bit participates in change
detection.

The client exposes explicit absence to its input controller, clearing predicted
selection, native-fire fences, optics and feedback. Loss of readiness remains
uncertain and retains its existing fail-closed behavior. Empty state refuses raw
and generated custom submissions, does not add an acquired wheel/cycle target,
and hides custom HUD/scope state. Late results still receive exact acknowledgments
without restoring selection or ownership. Action ingress requires a published
owned current life, preventing empty-life controls from waiting until a future
pickup.

The live handshake advances to protocol72. Native packet identities/bodies,
CompactIntent, public replica facts and native replay bodies are unchanged.
Reviewed status, timed-metadata and checkpoint allowlists include72; historical71
remains readable, and the movement-v3 threshold stays71. Old71 live connections
refuse normally. Custom replay remains blocked until CW-12.

This slice removes the remaining online spawn grant and supplies truthful empty
private state. It does not yet activate online world pickup acquisition. Next:
carry private knowledge from original input/control creation through genuine
accepted ingress, then connect authoritative contact, reliable item baselines and
owner funding publication. The earlier public combat fixture's automatic4/16
setup is now obsolete; it must acquire via actual contact before its full combat
matrix can qualify the corrected public mode. Developer combat coverage remains
separate. CW-05 is incomplete until that acquisition/delivery and device/refresh
acceptance is finished.
