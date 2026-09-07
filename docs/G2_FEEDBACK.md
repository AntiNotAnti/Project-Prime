# G2 authoritative combat feedback

This implementation adds client presentation for accepted server facts. It does not alter health, damage, aim, collision, score or authoritative attribution. `G2_ATTRIBUTION.md` describes the separate server assist/kill-event work. Live UI, native audio and Android device appearance remain unverified.

## Event boundary

Live: `AuthoritativePlay.BeforeSimulation` binds the scene's feedback object to accepted match, full local `Slot + ConnectionId + Life`, authoritative roster and server presentation tick. `DrainEvents` sends valid Combat events through feedback **before** filtering the entity subject. Dedicated Kill records enter the same scene object. Existing entity-specific effects then run once for the current subject identity.

Modern replay: `ModernDemoState` validates and buffers Kill records only for protocol 8; its bounded 256-record buffer joins the existing bounded Combat buffer at presentation. Snapshot/roster binding is the same. Protocol 5/6/7 playback does not invent structured kill records absent from those recordings. The kill buffer clears on discard, reset and match changes.

Combat and Kill use separate 512-ID modular dedup windows because the server shares event IDs across separately admitted reliable queues. Reordered IDs inside a family window are accepted once. A burst of Combat records cannot age an earlier Kill record out of its window. A match change resets both windows.

Names are cached from authoritative roster connection IDs (maximum 32 identities), not looked up by a possibly reused slot. Feed text is formatted on events and retained in four fixed entries. The feed displays each accepted kill for five seconds of presentation ticks, including attacker, victim, weapon, headshot, suicide/environment/team kill and supplied affinity/assist facts. Unsupported source distinctions are not inferred. A late event receives a fresh display interval on receipt while the damage-history timestamp retains the original authoritative tick.

## Markers and recap

Positive, non-silent Damage from the exact current local identity produces a normal or headshot marker. Misses and zero damage produce none. Kill confirmation comes from dedicated Kill, not predicted firing or local health inference. Reusing the same slot or entering a new life cannot receive an old life's attacker cue. Marker display is 12 ticks (18 for kill); a later ordinary hit cannot replace a still-active kill marker.

Settings persist through MenuSettings and the launcher settings page:

- Hit markers: Off / Visual / Visual + audio (default Visual)
- Headshot cue: On / Off (default On)
- Kill confirmation: On / Off (default On)

The optional audio reuses the existing short `LETTER_BLIP` asset, once per presented marker sequence; several accepted hits before one drawn frame coalesce into one cue. No new asset or audio mixer policy was added. The marker glyphs and colors need graphical acceptance on desktop and Android.

DamageHistory keeps eight received positive-damage records for the current local life. Silent damage stays in history but generates no hit-confirmation cue. Death displays the received killer, final weapon/amount when available, and the most recent four rows. New life clears it. Following a modern replay can show the same recap. No hidden world queries or guessed source positions are used.

## Directional damage

The old four-sector code is replaced by horizontal eight-sector quantization against normalized horizontal camera forward/right axes. Ignoring pitch avoids compressing the forward sectors when looking up or down. The existing cardinal sign convention and HUD nodes remain intact. Zero/nonfinite direction produces no invented direction; the old fallback lookup of an attacker by slot was removed because it could use a new occupant. Optional vertical hints are not implemented in this bounded pass.

## Verification

Focused run: 35 tests passed, covering feedback positive/silent/zero rules, headshot/kill settings, duplicate and reordered IDs, connection/life reuse, bounded feed/history, delayed receipt expiry, independent reliable families, all eight sectors and both sides of every boundary, modern Kill buffer validation, live accepted-fact recording, and oversized packet-wrapper scratch buffers.

The packet-buffer regression found during integration was an oversized demo scratch span passed through MatchTransitionPacket.Write into the newly exact-size MatchRulesWire writer. Recorder now passes its exact prefix; MatchTransitionPacket and JoinAcceptedPacket also bound their child slice, preserving their existing prefix-write contract. Tests assert their output prefix round-trips and the scratch suffix is untouched.

Command: `.NET 10 dotnet test tests/Tests/Tests.csproj -c Release --artifacts-path /private/tmp/codex-re-prime-g1/render-tests --filter 'FullyQualifiedName~CombatFeedbackTests|FullyQualifiedName~ModernKillRecords|FullyQualifiedName~LiveAcceptedFacts|FullyQualifiedName~PacketWrappersWriteOnly'`.

The focused test build compiled current Client and shared dependencies. Existing NU1903 for Tmds.DBus.Protocol 0.21.2 remained. Android explicit source links include the five feedback files and five radar files; APK/device validation is separate. Source and pure tests are not proof of live HUD layout, native audio quality, or match/replay UI readability.

## Retained life recaps

`RecapArchive` preallocates 16 life records, each capped at eight received damage rows. New local lives clear the live history but retain earlier records for this match and connection/slot. Match change or session/view-owner change clears the archive; another occupant cannot inherit it. Received facts format names at receipt. Reordered late Damage/Kill updates only an already retained matching full life identity, and dedicated Kill source information takes priority over an unknown damage weapon. An expired life with received damage but no death fact is labeled `Previous life damage`, not an invented elimination.

Post-match pages now include retained recaps after the three statistics pages; weapon cycle selects pages and automatic paging remains available. During modern replay, the configurable RecapHistory binding (default F6) toggles retained-life viewing and weapon cycle selects lives. No extra simulation input or damage mutation is generated. Archives remain bounded; records older than the latest16 are discarded. Replay records are available as their authoritative events are encountered, not reconstructed from unavailable events.

Focused recap+feedback+result tests pass37/37 (`/tmp/codex-re-prime-g1/g2-recaps.log`): respawn retention, late source-kind reconciliation, delayed death without inventing facts, duplicate suppression, capacity eviction, session/match clearing, and the earlier feedback/result cases. Android now supplies contextual RECAP/CLOSE and NEXT/PREV controls, tested through portable TouchControls; native event delivery and small-screen layout remain device acceptance gates.
