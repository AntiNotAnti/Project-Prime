# Momentum movement design provenance

Reviewed 2026-10-19 for PR-00. Momentum describes independently implemented
movement behavior; it is not Valve Source Engine code. No reference implementation,
Unity component, collision controller, tuning table or proprietary asset is imported.

| Reference | Review and decision |
| --- | --- |
| [Olezen/UnitySourceMovement](https://github.com/Olezen/UnitySourceMovement) | README credits Fragsurf by cr4yz/Jake E. The root LICENSE is MIT, copyright 2019 Ole Ellefsen Farner. Its README links `AwesomeX/Fragsurf-Character-Controller`; that upstream returned HTTP 404 during this audit, so upstream provenance is **not cleared for source reuse**. No implementation is copied. |
| [alpharaoh/SourceMovement](https://github.com/alpharaoh/SourceMovement) | Root tree contains a movement script and README, with no declared license; GitHub's license endpoint returns 404. No source reuse is permitted by this work. |
| [id Software Quake III movement](https://github.com/id-Software/Quake-III-Arena/blob/master/code/game/bg_pmove.c) and [default speed](https://github.com/id-Software/Quake-III-Arena/blob/master/code/game/g_main.c) | Reviewed behavior and public parameter facts for the separate private Quake III tuning draft. Source is GPL; no function bodies, translated implementation or collision code are reused. Existing independently implemented MomentumMath is retained; parameter scale is documented in the draft guide. |
| [id Software QuakeWorld PMove](https://github.com/id-Software/Quake/blob/master/QW/client/pmove.c) | Source header declares GPL version 2 or later, copyright 1996–1997 id Software. No function bodies or numeric tuning set are copied into this MIT project. |

The implementation specification supplies the mathematical contract: friction
reduces planar speed, acceleration adds along the desired direction subject to
available projected speed, and air acceleration caps that projection separately
from the acceleration factor. These equations will be implemented independently
using existing OpenTK vectors and standard C#. Test speeds are dimensionless
examples of the equations, not gameplay tuning imported from another engine.

Any future substantial source reuse requires a separate provenance review and
the applicable notices before it enters the repository. These links record design
references, not a claim of compatible licensing for their entire dependency trees.
