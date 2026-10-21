# Parkour v4 version contract

Protocol 80 deliberately selects Parkour v4 for short static ledge mantling.
Protocol 79 retains v3, 78 retains v2 and 77 retains v1. Classic remains the
default. Model IDs, modifier bit 19, packet widths and stance boundaries are
unchanged. Discovery, replay metadata and checkpoint allowlists include 80;
historical adapters keep their original semantics.

The mantle is an owner/native-bot velocity controller through existing 60 Hz
integration and native collision. Human movement remains owner-reported. Remote
puppets and passive replay consume accepted positions; they do not rerun mantle
admission or forces. No trajectory resimulation or hard movement enforcement is
introduced. Native abilities and triggers keep priority.

Admission requires fresh Jump with Forward, current safe static front contact,
full standing-body clearance along a bounded lift/across/settle route and one
safe flat convex face supporting the entire landing footprint. Ambiguous or
over-budget queries refuse. Rise to the support is at most 1.25 world units;
the lift's 0.06 clearance is included in that rise bound. Horizontal origin travel is at most 2.5 units and
includes two native radii plus landing margin. Assistance lasts at most 24 ticks.
Release Forward, body/ability changes, obstruction, stalled progress or timeout
ends assistance. Completion requires actual current native static support.
No Position or PrevPosition snap is used. Synthesized door/forcefield contacts
cannot supply a static seed. Their collision is absent from the polygon query,
so a bounded conservative enclosure check refuses nearby closed doors and active
fields along the complete route; native collision retains its existing response.

The passive checkpoint schema remains unchanged. It is not a resumable active
controller save; a future such save must persist mantle phase, route and clocks.
Qualification and known limitations are recorded in the movement results.
