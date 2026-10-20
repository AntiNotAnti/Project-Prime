# CW-05b — source-time native/custom ownership

The authority's custom source journal now records bounded historical selection
ownership independently of native weapon IDs, pose evidence and resource state.
`CustomNativeSelectionHistory` retains128 source frames and an anchor selection
before that window. Expired or foreign owner queries reject; an unsealed or future
source stays pending. The owner includes match, authority epoch, slot, generation
and life. A current `Selected` value alone cannot answer a recovered older shot.

Actual source observation does not seal native permission. The engine completes
input capture, binds primary controls and processes available actions before
sealing that phase. Queued Equip/Unequip keeps its source and following sources
pending until its terminal verdict. Accepted selection changes establish source
boundaries; denied controls leave prior ownership intact. Earlier native shots can
therefore retain eligibility after a later Equip, subject to existing native proof.

Permission release freezes the relevant ordering. Releasing native permission
prevents a later raw Equip at or before that source from rewriting its ownership.
A definite custom projectile emission similarly prevents same-source Unequip from
reopening native fire. A fresh later source can change selection. A definite
NoSpawn does not create that custom-emission fence or consume a round. Conflicting
selection actions receive a terminal SourceDenied before resource mutation; the
next action must use a new sequence. Neither permission release proves a native
projectile, pays native ammo, or bypasses source/pose proof.

Retirement cancels pending controls/resources while retaining sealed ownership
answers. Actual respawn retains the retired journal in the existing bounded32-life
store through the inclusive64-authority-frame native accepted-shot retention, and
longer while existing custom payments require retention. Future old-life queries
reject. Generation replacement keeps its existing immediate retirement policy.
This adds no unbounded history or once-per-life regrant route.

This slice supplies the authority ownership policy and engine/journal integration.
Native event admission, relay and recording do not consume it yet. The next slice
must stage unresolved events, filter rejected/staged events before admission and
relay, and release allowed historical events through native recovery. Public
selection remains gated until that integration and local native-fire suppression
are complete. No new live packet or native behavior is enabled here.
