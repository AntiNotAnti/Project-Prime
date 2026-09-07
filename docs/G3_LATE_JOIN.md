# Late joining

The server supports `-latejoin immediate|next|disabled`. Without an override,
Survival and Team Survival wait for the next match; other current modes admit
immediately. Custom rule construction retains its explicit compatibility default.

`next` admits the network session and sends roster, snapshots and world state,
but never creates a gameplay body. The waiting slot is excluded from quorum,
team balancing, collision, combat history and scoring. Input acknowledgements
continue, while attempts to leave spectator mode cannot activate the slot.
Protocol 8 marks its zero-health, inactive snapshot as `WaitingForMatch` plus
`Spectating`. The client locks Rejoin and displays a next-match notice.

A client that starts loading during countdown and becomes ready after play
starts is also held under `next`. `disabled` rejects new admission during
Playing/Ending/Intermission; sessions admitted before that boundary retain their
reservation. Rotation clears the wait and normal readiness/countdown activation
starts the next match. Waiting arrivals do not mutate an already captured result.

An actual participant has a bounded 1,800-tick (30-second) reconnect reservation
after disconnect. Its original slot/team and statistics remain reserved, while
the gameplay body and carried objectives are released. Reconnect requires the
previous connection ID, the same endpoint and unchanged hunter/name. New arrivals
cannot occupy a reserved slot. Expiry or rotation ends the reservation; an
eliminated Survival participant returns as a waiting spectator.

A public connection ID from a different endpoint cannot evict or reclaim a
session. This initial session continuity is deliberately limited to the same
socket endpoint; authenticated account recovery across endpoint changes belongs
to G4. Reconnecting uses a fresh connection/life epoch, not the old combat identity.

Validation is in progress. Waiting spectators currently reserve one of the eight
session slots; G5 true observers are a separate admission class outside that limit.
