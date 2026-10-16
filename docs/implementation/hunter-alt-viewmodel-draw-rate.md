# Hunter alt and viewmodel display-rate passes

This slice extends the existing read-only node-animation interpolation to ordinary alternate-form geometry and the first-person cannon. The exact camera late-latch remains in charge of viewmodel orientation; only the model's skeletal animation tracks are sampled at intermediate display frames. The render item copies its transform data before the canonical pose is rebuilt in `finally`.

Specialized Kanden Stinglarva segments are authored in world space by fixed-step gameplay, and Spire attack nodes determine collision and combat. Both retain their exact canonical draw behavior until dedicated immutable-pose shadow sampling can preserve collision and muzzle coordinates. No replay/netcode wire changes.

The same global `PROJECT_PRIME_ANIMATION_BLEND=0` and player override `PROJECT_PRIME_PLAYER_ANIMATION_BLEND=0` revert the new draw-only behavior. No new per-frame allocations in the hot path; sample values and node transforms are stack-only, conditional on verified recent ticks. Native 60/120/144/240 Hz and Android GLES captures remain required.
