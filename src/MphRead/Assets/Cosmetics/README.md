# Official cosmetics

The initial Obsidian and Alimbic skins are shared shader material treatments of
the user's native hunter textures. No Nintendo textures are distributed here.
Optional authored texture sets override individual materials without replacing
native recolors or changing geometry.

Add PNGs under:
`Skins/<Hunter>/<Skin>/<Biped|ViewModel|AltForm|Halfturret>/<ModelName>/<MaterialIndex>_<channel>.png`

Channels: `albedo`, `normal`, `specular` (R specular/G roughness), `emissive`.
Each channel is independently optional. Missing/corrupt assets fall back to native
materials. The scene owns uploads and shares them across players in that context;
shutdown frees them. PNGs are embedded as `Cosmetics/<relative path>` in desktop
and Android assemblies, so official content does not depend on user texture packs.
Only compiled catalog paths are eligible; no packet or saved selection is a path.

Armor and death visuals use deterministic procedural primitives and the scene's
bounded particle pool. Optional future masks/audio/icons belong under
`ArmorEffects/<Effect>/` and `DeathEffects/<Effect>/`.


## Modern texture residency

Authored skin channels use the shared `ModernTextureAsset` / `TextureAssetManager`
path. Source images may be up to 8192×8192. Runtime quality provides
Automatic/1K/2K/4K/8K ceilings, clamps to the GPU maximum, resamples normal maps
with renormalization, and builds mipmaps only when texture filtering enables
them. The same policy is used by HD material replacements and effect-model
assets. Missing or rejected authored assets retain the native-material fallback.
