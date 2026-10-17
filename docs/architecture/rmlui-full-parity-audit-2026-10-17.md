# Project Prime RmlUi historical parity audit
Date: 2026-10-17
Source of truth: `backup/main-2026-10-09` versus current `main` and RML-29..32 stacked candidates.

## Definition of parity

A page is **not complete** merely because `.rml` and `.rcss` compile.
It must have all of: exact route action and Back/focus ownership; real
authoritative data; correct permissions and asynchronous cancellation;
original foreground/background visual composition; desktop frame acceptance;
Android-specific input/lifecycle where applicable; and a supported fallback
when a backend is not migrated.

## Verified structural gaps and current implementation

| Area | Historical design | Current implementation / issue | Next acceptance |
| --- | --- | --- | --- |
| Home 3D Hunter | Actual engine mesh, suit, animated deployment chamber, accent and atmosphere | RML-29 / #692 restores the GL2 opt-in pipeline; not yet device-pixel validated | All seven Hunters, 4 suits, Focus/resize/re-entry, fallback, reduce motion |
| Lobby 3D formation | 8 pads, exact network roster, local front, distinct animated models | RML-29 restores per-slot previews, deterministic server roster and pad/label anchor table | Solo through eight clients, bots, churn, stale generation, team colors, 60/120/240Hz |
| Lobby data/actions | Ready count, teams, chat, match time/score, real map preview, owner actions | RML-30 / #693 binds authoritative chat, rules and team/roster fields. Actual thumbnail bitmap still missing | Owner/non-owner, team lock, chat length, map package, locale, DPI and no flicker |
| Standalone Hunter selection | Interactive real mesh/suit, cosmetics and preview camera | RML-31 / #694 restores isolated engine viewport using selected draft. Rotate/zoom/death controls still need porting | Biped/alt/death, cosmetics, all Hunters, fallback and GL state restoration |
| Home feature rail | Populated news dispatch and live session/social panel | RML-32 / #695 restores bundled News and real session state. Social stays unavailable, no invented metrics | News content, navigation, no dead buttons; separate authenticated Social backend |
| Native Social/Party | Friends, invitations, online players, presence, party ownership, privacy | `pages/social/social.rml` exists; actual service and route are missing on current main. Disabled visible entry is more truthful than a dead click | Account identity, invites, party lifecycle, block/privacy, offline retry, authorization |
| Community | Discovery, install/download, creator publish, revisions and cleanup | Native Discover is read-only. The working installer/transfer manager remains in existing Community workspace | Exact package hashes, durable transfer, no UI stalls, safe scene ownership |
| Hunter License | Customization, career/stats/history, achievements, account and privacy | Native overview/read-only subset; interactive full account and rewards pages still delegated | Authentication and opt-out privacy, pagination, cosmetic entitlement/recovery |
| Theatre/Replay | Full library, filtering, playback, clips, recovery, Studio editing | RML-28 provides native browse/search/favorite/watch; other operations use full legacy Studio | Historic protocol, map hash, validated file stamp, export fidelity, editor session ownership |
| Offline/Aim Lab | Match rules, bots, drills, training configuration | RML-25 and RML-26 native partial flows; advanced rules and specialized training remain legacy | Challenge presets, saved inputs, bounds, simulated match acceptance |
| Adventure | Three saves, campaign menus/mission flow, effects | RML-27 restores three-slot read/overwrite/resume. Full gunship/mission UI still delegated | Existing saves, unreadable protection, no destructive overwrite, campaign flow |
| Native Studio | Creator/Map/Replay launch, workspace lifecycle, data handoff | Native entry exists but production feature parity remains outside current launcher | Background process transfer coordination, editor save/preview, data handoff |
| In-game/pause/results/HUD | The original RML shell and composition contracts | Authored assets and typed intents exist, but the new desktop opt-in host still prioritizes Home/Play/Lobby and fallback routes | Real pause, rematch, vote, replay, HUD and device input tests |
| Android | RML resources, draw-list ABI, GLES presentation, touch/IME, lifecycle | RML-17..19 build opt-in assets and compositor, not a finished default Android launcher | APK device boot, touch, IME, background/foreground, thermal/frame pacing |
| Full Settings | Category-based reversible graphics/audio/input/network controls | **Deliberately deferred.** Existing working Settings remains the owner until every other page is migrated | FINAL slice: video rollback, failure/recovery, user profiles, safe persistence |

## Recommended review/implementation order

1. **RML-29..32**: complete Home/Lobby/Hunter 3D and currently authored P0
   controls. Merge only after CI, keep opt-in and screenshot against the
   October 9 build. Do not infer visual parity from source presence.
2. **RML-33**: full Home Social and Lobby Social backed by real current
   account service, not placeholder counts. Port the service explicitly if
   missing, with privacy gates.
3. **RML-34**: authenticated Community install, durable transfer progress,
   local map identity, and creator workflows.
4. **RML-35**: Hunter License/account/career/customization, all interactive tabs,
   current model-preview materials and proper error state.
5. **RML-36**: Replay/Studio and Adventure rich-screen parity, and editor
   process ownership / transfer index.
6. **RML-37**: full in-game/pause/results/HUD/back/focus contract.
7. **RML-38**: Android surface/touch/IME/resume and desktop backend visual
   acceptance, native fallback and incremental performance benchmarks.
8. **FINAL**: full Settings migration, then explicit default UI cutover.

## Invariants

- No invented game/roster/network authority or optimistic server admission.
- Preserve immutable installed Community map package identity.
- Keep historical replay readability and studio external-process ownership.
- No renderer recreation per page or per frame; keep one scene-free side
  Scene for menu Hunter preview and one GL context.
- All mutable drafts are versioned and cancellable. A hidden/retired modal
  cannot deliver stale commands.
- Do not merge any source merely because a textual `check-pages.py`
  inspection passes. Capture visual evidence across source and target
  dimensions, verify frame timing and GPU resource stability.
- Full Settings **last**.
