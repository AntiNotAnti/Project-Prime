# RML-21: native lobby Hunter and suit identity

The October 9 backup's RmlHunterSelectionPresenter depended on its
LobbySessionController and HunterSelectionController, both absent from the
current owner-thread RmlUi menu seam. Copying it without current authority
and preference semantics would regress the active NetSession lobby.

## Ported behavior

- The lobby's Hunter button opens a focused RmlUi native modal displaying
  seven playable Hunters and four suit variants.
- The allowed Hunter pool is fetched from the current authoritative
  session's HunterRules.Pool match configuration. Low-tier restrictions are
  enforced both in native button bindings and on the service request.
- Changing Hunter/suit is a local selection only until pressing Apply.
  Cancel and Back/Escape discard that state and never call SendIdentify.
- Before Apply, the controller compares the live session's MatchId and
  AuthorityEpoch with the captured selection, checks server command state,
  and revalidates Hunter permissions. The live service repeats these checks
  and persists LauncherPrefs then sends one NetSession.SendIdentify request.
  Sending Identify does **not** imply that the remote server acknowledged it.
- A changed session or authority removes the old modal. The existing
  page manager rejects stale events from retired documents.
- This new compact page intentionally does not display unsupported
  spectator/team/cosmetics/animation-preview buttons. Rich historical Hunter
  customization remains routed through the current existing screens.

## Verification

The presentation-neutral test fixture verifies lobby availability,
restricted Hunter rejection, selected/disabled bindings, draft changes,
single Apply, cancellation, epoch fencing, and stale-modal retirement.
The existing live-menu OpenGL test and build matrix remain required.

## Remaining work

Full Hunter cosmetics, native 3D preview and model lifecycle,
spectator/team configuration, complete Settings and Lobby Admin are
separate follow-up slices. Android remains opt-in pending RML-19 launcher
surface/touch/IME/lifecycle integration and real-device verification.
This slice is not a default RmlUi cutover.
