# Graphics presets

Presets share one immutable recipe in `GraphicsPresetProfile`. The settings
screen reads that recipe into its draft; `RenderOptions` applies it to the
renderer. Saved individual settings remain authoritative, so reselect a preset
and save to adopt these recipes in an existing installation.

| Preset | Render scale | AA | Sharpening | Bloom | AO | Directional shadows |
| --- | --- | --- | --- | --- | --- | --- |
| Original | 100% | Off | 0% | Off | Off | Off |
| Performance | 100% | FXAA | 10% | Off | Off | Off |
| Enhanced | 100% | SMAA | 10% | 20% | Low | Off |
| Ultra | 150% | SMAA | 5% | 20% | Low | High |
| Extreme | 200% | SMAA | 0% | 20% | Low | Ultra |

All tiers retain original lighting, fog and neutral color controls. Performance
uses filtered mipmaps and 4x anisotropy; Enhanced and above use 16x anisotropy
and support optional material maps. Texture replacement selection is preserved,
except Original disables replacements. Original keeps unfiltered source pixels.

The stronger presets spend their budget on sampling and shadow detail instead
of increasing effect strength. 200% is twice each dimension (four times the
native pixel count); the old 400% Extreme rendered sixteen times as many pixels.
Performance now stays native to avoid combining sub-native softness with FXAA;
users needing a lower fill-rate budget can still reduce render scale manually.

The shader's dynamic glow lowers bloom's threshold from 0.62 to 0.48 and adds
intensity, including on ordinary bright surfaces. Extra fog also layers over
map-authored fog, and cinematic grading adds saturation before the saturation
slider. Presets therefore leave those effects off rather than stacking them.
PBR, screen-space reflections, HDR tone mapping, depth-derived relief lighting,
contact shadows, extra fog, glow, color grades and pixel-art upscaling remain
available as custom choices. No shader behavior is changed by these recipes.

For reproducible captures without modifying saved settings, pass
`-graphicspreset Enhanced` (or another preset name) with a thumbnail command.
Run `-graphicscheck` to verify recipe application, clearing prior custom effects,
draft isolation, migration and texture upscaling.
