# Game-mode implementation status

This branch implements the supplied game-mode architecture and all requested new modes and modifiers.
The automated checks below cover gameplay, lifecycle contracts, frozen joins, replay restoration,
map resources, UI, and platform compilation. Live rendered multiplayer playtesting and integration
with the separate Enhanced Hunters branch remain release acceptance work.

## Implemented

- One ConfigureMatchMode entry point selects the handler and team mode, optionally applying defaults.
- Round reset clears all eight player/team rows and weapon/objective/report counters, preserving configuration.
- Room rotation captures the server definition and changes rules at the synchronous load boundary.
  Incoming packets cannot apply the next round's rules to the fading old room.
- AutoReset settings no longer read PointGoal. Offline, lobby and dedicated server paths carry the rule.
- Protocol 29 separates ushort lobby flags from uint match modifiers. Session and status packets,
  lobby commands, recorded-session conversion and decoder checkpoint sizing are updated.
- Central modifier validation is shared by the lobby UI and server.
- MapModeCapabilities inspects the selected native layer and validates custom source capabilities.
  Room construction and server starts reject unsupported objectives; votes and offline selection provide feedback.
- ResourceAudit distinguishes unsupported layers from incomplete objective layouts.
- Objective match-point text is mode-specific. Defender and Prime Hunter have 30/10-second victory warnings.
- Prime ownership cleanup no longer clears shot deduplication. Headless Prime HUD animation tolerates absent HUD assets.
- Disabled time targets no longer end Defender/Prime Hunter matches immediately.
- Career rating eligibility explicitly excludes bot rounds and custom combat rules.
- A missing baseline OBJ importer was recovered from the original workspace. Its source directory was
  accidentally covered by the repository's generic Obj intermediate-output ignore rule; exceptions retain it.

- Relic uses the neutral Bounty layer, a 90-second hold target and a 10-minute default round.
  Carrier time survives drops, only a living active carrier earns time, deposits do not score,
  and carrying prevents morphing. Native and Pro HUDs display hold time; the objective HUD
  identifies the carrier. Mode selection, ranking, map capability checks and replay rule decoding
  recognize Relic. It reuses existing Time arrays and flag state rather than adding checkpoint fields.
- Live clients now apply server objective facts from the already-transmitted ReplayWorld stream.
  Match, authority, room and player generation/life are checked. Clients stop authoring flag/node
  outcomes and Prime/Relic time. Replay application remains isolated to private replica scenes.
  A separate bounded objective bootstrap is tied to the same frozen authority frame as the
  player lanes. WorldReady waits for atomic assembly and application; older periodic facts
  cannot overwrite the baseline. It includes modifier ammo and token entities.
- Create Server rotation and lobby map pickers show incompatibility reasons. Create Server validates
  the final rotation again; offline launch checks compiled entity content and declared capabilities.
- Lobby rule controls are grouped into Match, Gameplay and Advanced, with team-only controls hidden
  in FFA and objective-specific controls shown only for their relevant modes.

- Hardpoint and Hardpoint Teams rotate through real nodes every 60 seconds, honor custom
  HardpointOrder metadata, dim inactive radar contacts, and award only active uncontested hold time.
- Gun Game uses the seven-weapon ladder, preserves progression on death, blocks weapon pickups,
  and requires an Imperialist kill to complete the final stage.
- Fiesta derives two spawn weapons from match/life/slot identity. One in the Chamber grants
  one precision shot, normal Power Beam fallback, OHKO precision damage, and a shot per confirmed
  kill. Owner-reported ammo cannot replenish shots; hit claims require an authority-fired launch.
- Kill Confirmed and its team variant use bounded, replicated 20-second tokens with confirms
  and denials. Headhunter carries token value, drops it on death, and banks through Bounty base
  collision volumes. Token identities, values, lifetimes and counters survive bootstrap and replay.
- Replay authority-world version 4 retains legacy v1–v3 reads, includes mode/report facts, and
  has bounded reusable capture buffers. Checkpoint accessors and gameplay hashes include new state.
  Timeline events cover Relic transfers, Hardpoint changes, ladder progression and token actions.
- Post-match report rows carry four mode-specific values. Historical protocol 24–28 reports
  receive neutral defaults. Results show objective statistics instead of the generic secondary line.
- Lobby and offline rules use three columns (Match, Gameplay, Advanced) without scrolling.
  Existing keyboard/controller controls and apply/validation behavior are retained.

## Verification

Run the cheap suite without extracted assets:

```
dotnet run --project tools/nettest -- --gamemodecheck
dotnet run --project tools/nettest -- --advanced-rules
```

The application also exposes `-gamemodecheck`, `-advancedrulescheck`, and combined
`-matchrulescheck` before game-file setup. Asset-backed commands are
`-gamemodecheckscene`, `-advancedrulesscene` and `-resourceaudit`.

Asset-backed nettest entry points accept a directory containing paths.txt:

```
dotnet run --project tools/nettest -- --gamemodecheck-scene DATA_DIRECTORY
dotnet run --project tools/nettest -- --advanced-rules-scene DATA_DIRECTORY
dotnet run --project tools/nettest -- --resourceaudit DATA_DIRECTORY
```

Validated during this pass:

- Lifecycle/modifier contracts: 2,024 checks.
- Production authority/objective handlers: 789 checks at 2, 4 and 8 players.
- Advanced Rules: 119 checks; asset-backed loadout suite: 277 checks across every compatible base mode.
- UDP lobby/lifecycle suite: 6,886 assertions.
- Replay format: 2,734 checks.
- Native resource audit: 924 layouts, zero broken combinations.
- All 19 selectable modes pass frozen objective bootstrap, lane loss/reordering/duplicates,
  re-admission, spectator-state delivery, and transition to a fresh player life.
- All 19 modes pass 1,801 gameplay/presentation comparisons each, seven checkpoint restores
  each, and 61 file/clip seek comparisons each. Token-mode fixtures include live token facts.
- Rules/UI suite: 18,181 checks. Both rules dialogs were rendered and visually inspected;
  the lobby sheet was also checked at 960×540 without scrolling.
- Windows x64, Linux x64, Android and dedicated-server builds succeeded. These are compilation/package checks,
  not native gameplay acceptance on all three platforms.

The authority suite exercises real handlers for score/time victory, kills, suicides,
survival, Octolith pickup/scoring/death reset, node occupancy/capture/contesting/scoring,
and round reset. It does not claim the entire requested gameplay/lifecycle matrix:
rendered cross-mode rotations, every spectator/JIP combination, and all objective edge
cases still need dedicated acceptance coverage.

## Integration and acceptance limits

- All new modes remain excluded from Hunter License progression until separately approved.
  Bot-containing rounds and Fiesta/One in the Chamber remain excluded too.
- Bot targeting supports the active hill, current weapon/loadout, collectible tokens and banking
  bases. Competitive route quality and balance still require actual matches on representative maps.
- Compiling Windows/Linux/Android does not constitute native gameplay acceptance on those devices.
- Automated frozen joins and replay fixtures are not a substitute for a rendered multiplayer soak
  covering every map-to-map transition, spectator preference transition and custom package transfer.
- EnhancedHunters is reserved in this branch; its separate implementation in the original checkout
  must be integrated before enabling that modifier. Both branches changed protocol 29, so their
  contracts must be reconciled before integration; equal version numbers do not imply compatibility.
