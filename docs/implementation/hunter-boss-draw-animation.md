# Display-rate Hunter and boss poses

* All seven standard third-person bipeds share PlayerDraw's two-slot skeleton path. This change samples lower/upper separately at the draw-rate phase without touching fixed-step frames or client/server aim inputs.
* **Critical fix:** SetModel(same model) no longer destroys PresentationSamples on each player draw. Actual model/LOD changes still reset.
* The inherited generic EnemyInstance path (including Gorea DrawGeneric bodies) now takes the existing guarded sampled-transform path. The draw queue copies the palette, and canonical transforms are restored afterward. Replays retain their own interpolation timeline.
* Highly specialized enemy paths that override drawing entirely (Alimbic turret, Cretaphid and some effects) still require their own separately reviewed geometry-pass implementation, because they compute gameplay positions from node matrices. They are not claimed complete by this PR.
* Alternate forms and the first-person gun keep the canonical draw path in this slice. The split biped hook is enabled via the existing global default-on switch, with `PROJECT_PRIME_PLAYER_ANIMATION_BLEND=0` or `PROJECT_PRIME_BOSS_ANIMATION_BLEND=0` as targeted overrides.
* Regression covers same-LOD animation clock preservation; native all-Hunter/LD/solo+8-player/replay parity and real device tests are still necessary. No wire/serialized-state changes.
