# G3 WAN and match-flow acceptance

G3's network and match-flow implementation is present in the current tree. The
external Internet gate was not independently run in this turn; per the owner's
direction it is recorded as assumed good for administrative plan completion.

## Current protocol and policy

- Live authoritative wire family is 2, protocol 8. Discovery is read-only and
  status extension v4 reports `Lobby`, `Countdown`, `Playing`, `Ending`, or
  `Intermission`, the authoritative join disposition, lobby/spectator/ready
  counts, bots, and Ranked/tournament locks.
- Remote interpolation remains fixed at six ticks / 100 ms. The adaptive
  experiment failed its declared asymmetric-network quality gates and is not the
  default.
- Late-join, reconnect grace, team allocation, overtime, reliable world events,
  chat, intermission voting, and post-match lobby transitions remain server-owned.
  A disconnected participant has a bounded 1,800-tick grace path when the
  existing identity/reconnect proof is valid; a public roster identity alone is
  not reconnect authority.

## Recorded evidence

The recorded loopback impairment runs used real AMHE1 content, eight UDP clients,
100 ms RTT, ±20 ms jitter, and 2% loss for 300 seconds. Both compensation ON and
OFF runs retained eight peers with zero server/client dropped ticks and zero
reliable overflow or transport drops. They are headless loopback measurements,
not external WAN evidence.

The final validation at `1c8df59` also records focused admission, reconnect,
late-join, overtime, world-event, browser, chat, vote, lobby/session, and status
coverage. The main suite is 1122/1122 with extracted AMHE1, Backend is 198/198
against isolated PostgreSQL with no skips, and the UI acceptance run is 14/14
with 68/68 deterministic captures. These are local/source and loopback results,
not external WAN evidence.

## External WAN disposition

| Planned condition | Status |
| --- | --- |
| 40–80 ms normal Internet path | Owner-assumed/waived; not independently run. |
| 100–150 ms, jitter, 2–5% loss, and 1–3 second stall | Owner-assumed/waived; not independently run. |
| Join/countdown/combat/overtime/late join/reconnect/spectator/map transition | Owner-assumed/waived for external endpoints; focused local coverage remains recorded. |
| Intermission vote, chat, and blocked report outbox | Owner-assumed/waived for external endpoints; source and focused checks remain available. |

The owner assumption is not a WAN log, latency sample, or proof of NAT,
firewall, ISP, or multi-endpoint behavior. No external address, credential, or
deployment result is invented here.

## Retained limits

Protocol 8 remains unreleased and all clients, match servers, and directories
must be upgraded together. A later WAN record should preserve the fixed six-tick
presentation policy unless a separately approved experiment changes it. The user
skipped external WAN, physical Android/high-refresh, 30-second/16-observer, and
combined endurance gates; they remain owner-assumed administrative acceptance,
not measured WAN or device proof.
