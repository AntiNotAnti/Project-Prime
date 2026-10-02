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
uses filtered mipmaps and 4x anisotropy for native cartridge textures; Enhanced
and above use 16x there and support optional material maps. Texture replacement
selection is preserved, except Original disables replacements. Original keeps
unfiltered source pixels.

HD/authored 3D assets have a separate sampling policy so selecting a 4K/8K source
cannot silently leave distant detail undersampled. **Auto** is the default and
uses linear magnification, trilinear minification, a complete mip chain, LOD
bias 0 and bounded anisotropy (8x desktop, 4x Android, clamped by the device).
**Legacy pixel** preserves nearest-neighbour HD sampling. **Custom** makes HD
assets follow the existing filtering, mipmap and anisotropy controls. UI/fonts
stay outside this HD material policy; authored effect models use it.

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

## Runtime changes and cost

Press Apply Changes to update the current renderer. Source texture upscale and
HD replacement or HD sampling-policy changes reupload loaded model textures in
place, retaining binding IDs; turning replacements off restores cartridge pixels
and releases replacement companion maps. Modern replacement mip chains are built
during upload instead of the first frame that sees the material, avoiding a
first-look mip-generation spike. Native cartridge textures retain lazy mip
generation so the legacy path pays no extra upload cost. Filtering/mipmaps/
anisotropy for native textures and post-process settings apply on subsequent
draws without a restart.

At 500 FPS the frame budget is 2 ms; at 250 FPS it is 4 ms. An extra 2 ms pass can
therefore halve the FPS counter. Render scale is per dimension: 150% costs 2.25x
pixels, 200% costs 4x, and 400% costs 16x. Deferred PBR replays opaque geometry;
directional shadows render another geometry pass, and reflections/occlusion add
per-pixel samples. Actual impact depends on the scene and GPU/CPU bottleneck.

Off-state paths skip shadow/PBR passes, post-processing when no effect needs it,
and depth-normal reconstruction when no enabled effect consumes normals. Zero
bloom intensity skips its work. Disabling post-processing invalidates temporal
history. TAA and dynamic lighting request readable depth explicitly. Inactive
cosmetic uniforms are cached per shader program; disabled material maps avoid
companion texture-unit work. Cached framebuffer allocations can remain for reuse;
retaining an allocation does not execute its rendering pass.

`-graphicscheck` exercises individual off transitions. `-thumbnailwindowcheck`
compiles/links the shaders in an actual desktop GL context.
`-cosmeticpreviewcheck <directory>` also tests live 2x/4x texture uploads back to
native GPU dimensions, replacement companion cleanup, stable texture handles,
and cosmetic shader enable/off/re-enable transitions on the real hunter preview.
