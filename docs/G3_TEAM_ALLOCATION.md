# Team allocation

Admission now chooses the smaller team. Loading peers reserve their assignment,
so simultaneous joins cannot all select the same empty team. Equal counts use
slot parity only as a deterministic tie break. Casual score-based selection is
available in the pure allocator but is not enabled by a server policy yet.

Before play, ready participants are balanced with the minimum number of moves,
highest slot first. A changed countdown returns through the lifecycle owner and
resets the world, spawns and input epoch before starting its new deadline. No
automatic reassignment runs in Playing, Ending or Intermission. Rotation assigns
occupied slots in sequence, so holes left by disconnects do not bias a team.

Validation: all 6,561 absent/orange/green configurations satisfy minimum movement,
determinism and a count difference of at most one. Loopback admission and phase
gating pass. Real AMHE1 match-phase and simulation-ordering fixtures pass with the
new pre-countdown balancing expectation. Party, rating and tournament roster
constraints remain future policy work.
