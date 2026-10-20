# RmlUi experience improvements — 20 October 2026

## Home and Lobby

Home now gives the engine-rendered Hunter a larger central composition. Activity selection and deployment stay at the bottom of the fixed left rail; Dispatch remains reachable even on short windows. Party and friends share one secondary scroll region. The existing routes, service bindings and four party seats remain intact.

Lobby now presents compact match facts on the left, all eight initial roster rows on the right at desktop size, a separate chat history, and persistent bottom match controls. Time and score open the existing authority-aware rules editor. Short touch and large-text windows scroll the right column when necessary, preserving every roster row and chat action. At 640 pixels the center prioritizes the local Hunter; some outer remote silhouettes sit behind the rails.

The shared shell uses a compact single navigation row at 640 pixels, with a separate utility row for large text. The Studio label shortens without changing its accessible name.

![Windows Home](rmlui-experience-2026-10-20/home-windows.png)
![Windows eight-pad Lobby fixture](rmlui-experience-2026-10-20/lobby-windows.png)

## Validation

Tested on DESKTOP-94724L4, NVIDIA RTX 5080, OpenGL 4.6. Native DLL SHA256: `e87766fc9d48beac8485a46fb092cb61e6c3d3136015c0e025869735cf5d80bb`. Isolated user data referenced the user's extracted AMHE1 game assets, with no production settings or saves copied. Screenshots use explicit local fixtures, not live players or accounts.

- Authored asset validation: 150 markup/style files.
- Native CPU layout: 87 documents, 12 viewport/accessibility cases; 2,669 checks, zero failures. Nine detailed fixtures produced 81 native framebuffer screenshots. The Lobby fixture now checks that all eight initial roster rows fit on desktop without scrolling.
- Actual Hunter meshes, Home party pads and eight-pad Lobby: passed at 1280×720 and 640×360 (693,230 and 143,053 assertions respectively; assertion totals include per-frame invariants, not distinct workflows).
- Actual Lobby interactions: passed at 1280×720 and 800×600, including authority-aware rules drafts, readiness, teams, maps and chat.
- Studio workspace suite: passed, including Home rails, full settings and version management.
- Corrected one stale acceptance assertion to recognize the already-existing Riftline M7 rule at index 18 and reject index 19.

The compact touch classes and large text were exercised on Windows; this is not a claim of physical Android-device qualification. Mac fallback was unnecessary for this phase. Detailed local reports and captures are retained under `artifacts/rmlui-experience-20261020/phase1-final`.
