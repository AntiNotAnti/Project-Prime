# Cosmetic decal artwork

Run `python3 tools/cosmetic-art/build_decals.py` to rebuild the 14 PNG sheets.
The script uses only Python's standard library. Its explicit paths define the
original artwork: Obsidian uses technical seams, racing stripes, and seven
hunter insignia; Alimbic uses angular circuit inlays and framed insignia.

The transparent 128 × 128 sheets are embedded in the game assembly. At first
use, the renderer composites a sheet over the player's locally extracted native
material, preserving its alpha. The result is cached per skin/model/material/
context and freed with the scene's cosmetic textures. No extracted game textures
are included in this directory or in the shipped cosmetic assets.

These are authored UV decal treatments, not individually hand-painted texture
atlases. Material-specific `_albedo.png` assets still take precedence. Team
matches retain the existing native-palette preservation path. The same authored
sheets are used on biped, weapon, and alternate-form materials.
